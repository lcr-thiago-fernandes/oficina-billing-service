using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Oficina.Billing.Aplicacao.Portas;

namespace Oficina.Billing.Infraestrutura.Simulador;

/// <summary>
/// Gateway em memoria para testes, ambiente local e demonstracao (<c>Pagamento:Modo = Simulado</c>). Singleton: os
/// pagamentos registrados pelo endpoint do simulador ficam disponiveis para a consulta feita no processamento.
/// </summary>
public class GatewayPagamentoSimulado(IOptions<OpcoesPagamento> opcoes) : IGatewayPagamento
{
    public const string DetalheRecusado = "cc_rejected_simulado";
    public const string Meio = "simulado";

    private readonly ConcurrentDictionary<string, PagamentoGateway> _pagamentos = new();
    private long _sequencia;

    public string Nome => "Simulado";

    public Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken ct) =>
        Task.FromResult(new CobrancaCriada($"sim-pref-{cobranca.OrdemServicoId}",
            $"https://simulador.local/pagamentos/{cobranca.OrdemServicoId}"));

    /// <summary>Registra um pagamento ja decidido e devolve o id (<c>sim-{n}</c>) a ser notificado.</summary>
    public string RegistrarPagamento(Guid osId, decimal valor, bool aprovado)
    {
        var id = $"sim-{Interlocked.Increment(ref _sequencia)}";
        _pagamentos[id] = new PagamentoGateway(id, osId,
            aprovado ? StatusPagamentoGateway.Aprovado : StatusPagamentoGateway.Recusado,
            aprovado ? null : DetalheRecusado, valor, Meio);
        return id;
    }

    public Task<PagamentoGateway?> ObterPagamentoAsync(string pagamentoId, CancellationToken ct) =>
        Task.FromResult(_pagamentos.GetValueOrDefault(pagamentoId));

    public Task<EstornoGateway> EstornarAsync(string pagamentoId, decimal valor, string chaveIdempotencia,
        CancellationToken ct)
    {
        if (opcoes.Value.SimularFalhaEstorno)
            throw new GatewayPagamentoIndisponivelException("Falha de estorno simulada (Pagamento:SimularFalhaEstorno)");

        _pagamentos.AddOrUpdate(pagamentoId,
            _ => throw new InvalidOperationException($"Pagamento simulado {pagamentoId} nao encontrado"),
            (_, p) => p with { Status = StatusPagamentoGateway.Estornado });
        return Task.FromResult(new EstornoGateway($"sim-estorno-{pagamentoId}", valor));
    }
}
