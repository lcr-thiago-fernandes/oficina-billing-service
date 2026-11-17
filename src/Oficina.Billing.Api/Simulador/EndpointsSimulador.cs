using Oficina.Billing.Api.Configuracao;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Infraestrutura.Simulador;

namespace Oficina.Billing.Api.Simulador;

public record PagamentoSimuladoRequisicao(string? Resultado);

public record PagamentoSimuladoResposta(string PagamentoId, string Resultado);

/// <summary>
/// Substitui o checkout do Mercado Pago em ambiente local/demonstracao: registra um pagamento ja decidido no gateway
/// simulado e o processa pelo mesmo caso de uso do webhook. So e mapeado com <c>Pagamento:Modo = Simulado</c>.
/// </summary>
public static class EndpointsSimulador
{
    public const string Rota = "api/v1/simulador/pagamentos/{osId:guid}";
    private const string Aprovado = "aprovado";
    private const string Recusado = "recusado";

    public static IEndpointRouteBuilder MapearSimulador(this IEndpointRouteBuilder app)
    {
        app.MapPost(Rota, RegistrarPagamentoAsync)
            .RequireAuthorization(PoliticasDeAutorizacao.RequerAdmin)
            .WithTags("Simulador")
            .Produces<PagamentoSimuladoResposta>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }

    private static async Task<IResult> RegistrarPagamentoAsync(Guid osId, PagamentoSimuladoRequisicao requisicao,
        IRepositorioBilling repositorio, GatewayPagamentoSimulado gateway,
        ProcessarNotificacaoPagamentoCasoDeUso processar, CancellationToken ct)
    {
        bool aprovado;
        switch (requisicao.Resultado)
        {
            case Aprovado: aprovado = true; break;
            case Recusado: aprovado = false; break;
            default:
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Resultado invalido",
                    detail: $"Use \"{Aprovado}\" ou \"{Recusado}\".");
        }

        var orcamento = await repositorio.ObterOrcamentoAsync(osId, ct)
            ?? throw new OrcamentoNaoEncontradoException(osId);
        if (orcamento.Status != StatusOrcamento.Aprovado)
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Orcamento nao aprovado",
                detail: $"O orcamento da OS {osId} esta {orcamento.Status}; o pagamento exige orcamento Aprovado.");

        var pagamentoId = gateway.RegistrarPagamento(osId, orcamento.ValorTotal, aprovado);
        var resultado = await processar.ExecutarAsync(pagamentoId, ct);
        return Results.Ok(new PagamentoSimuladoResposta(pagamentoId, resultado.ToString()));
    }
}
