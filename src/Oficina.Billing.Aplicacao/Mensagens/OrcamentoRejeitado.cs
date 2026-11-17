using MassTransit;

namespace Oficina.Billing.Aplicacao.Mensagens;

[MessageUrn("oficina:saga:OrcamentoRejeitado")]
[EntityName("oficina-saga-OrcamentoRejeitado")]
public sealed record OrcamentoRejeitado : IMensagemSaga
{
    public Guid OrdemServicoId { get; init; }
    public DateTime OcorridoEm { get; init; }
    public Guid OrcamentoId { get; init; }
    public string? Motivo { get; init; }
}
