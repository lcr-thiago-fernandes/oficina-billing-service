namespace Oficina.Billing.Aplicacao.Portas;

/// <param name="EventoTipo">"PagamentoConfirmado", "PagamentoRecusado" ou "Nenhum".</param>
public record RegistroWebhook(string PagamentoId, Guid OrdemServicoId, string EventoTipo, string? Motivo,
    bool EventoPublicado, DateTime CriadoEm);
