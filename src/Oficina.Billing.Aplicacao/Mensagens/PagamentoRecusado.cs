using MassTransit;

namespace Oficina.Billing.Aplicacao.Mensagens;

[MessageUrn("oficina:saga:PagamentoRecusado")]
[EntityName("oficina-saga-PagamentoRecusado")]
public sealed record PagamentoRecusado : IMensagemSaga
{
    public Guid OrdemServicoId { get; init; }
    public DateTime OcorridoEm { get; init; }
    public string? PagamentoId { get; init; }
    public string Motivo { get; init; } = null!;
}
