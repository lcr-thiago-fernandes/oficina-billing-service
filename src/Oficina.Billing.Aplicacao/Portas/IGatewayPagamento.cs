namespace Oficina.Billing.Aplicacao.Portas;

public interface IGatewayPagamento
{
    /// <summary>"MercadoPago" ou "Simulado".</summary>
    string Nome { get; }

    Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken ct);

    Task<PagamentoGateway?> ObterPagamentoAsync(string pagamentoId, CancellationToken ct);

    Task<EstornoGateway> EstornarAsync(string pagamentoId, decimal valor, string chaveIdempotencia, CancellationToken ct);
}
