namespace Oficina.Billing.Aplicacao.Telemetria;

/// <param name="Etapa">OrcamentoGerado, OrcamentoFalhou, OrcamentoAprovado, OrcamentoRejeitado, OrcamentoCancelado, PagamentoConfirmado, PagamentoRecusado, PagamentoEstornado, EstornoFalhou, EstornoAutomatico.</param>
/// <param name="Resultado">Sucesso ou Falha.</param>
public record EventoPagamento(Guid OrdemServicoId, int NumeroOs, string Etapa, string Resultado, decimal? Valor,
    string? Meio, string Gateway);
