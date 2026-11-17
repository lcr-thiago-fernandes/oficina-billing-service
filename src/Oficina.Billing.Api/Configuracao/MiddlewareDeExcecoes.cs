using Microsoft.AspNetCore.Mvc;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Dominio.Comum;

namespace Oficina.Billing.Api.Configuracao;

/// <summary>Traduz excecoes da aplicacao e do dominio em <c>application/problem+json</c>.</summary>
public class MiddlewareDeExcecoes
{
    private readonly RequestDelegate _proximo;
    private readonly ILogger<MiddlewareDeExcecoes> _log;

    public MiddlewareDeExcecoes(RequestDelegate proximo, ILogger<MiddlewareDeExcecoes> log)
    {
        _proximo = proximo;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await _proximo(contexto);
        }
        catch (Exception ex) when (!contexto.Response.HasStarted)
        {
            var problema = Mapear(ex);
            if (problema.Status >= StatusCodes.Status500InternalServerError)
                _log.LogError(ex, "Falha ao processar a requisicao: {Titulo}", problema.Title);
            else
                _log.LogWarning(ex, "Requisicao recusada: {Titulo}", problema.Title);

            contexto.Response.StatusCode = problema.Status!.Value;
            await contexto.Response.WriteAsJsonAsync(problema, options: null, contentType: "application/problem+json", cancellationToken: contexto.RequestAborted);
        }
    }

    public static ProblemDetails Mapear(Exception ex) => ex switch
    {
        OrcamentoNaoEncontradoException => Problema(StatusCodes.Status404NotFound, "Nao encontrado", ex.Message),
        TransicaoInvalidaException => Problema(StatusCodes.Status409Conflict, "Transicao invalida", ex.Message),
        ConcorrenciaException => Problema(StatusCodes.Status409Conflict, "Conflito de concorrencia",
            "O recurso foi alterado por outra operacao. Tente novamente."),
        GatewayPagamentoIndisponivelException => Problema(StatusCodes.Status502BadGateway,
            "Gateway de pagamento indisponivel", "Nao foi possivel falar com o gateway de pagamento. Tente novamente."),
        DominioException or ArgumentException => Problema(StatusCodes.Status422UnprocessableEntity,
            "Operacao invalida", ex.Message),
        _ => Problema(StatusCodes.Status500InternalServerError, "Erro interno",
            "Erro inesperado ao processar a requisicao."),
    };

    private static ProblemDetails Problema(int status, string titulo, string detalhe) =>
        new() { Status = status, Title = titulo, Detail = detalhe };
}
