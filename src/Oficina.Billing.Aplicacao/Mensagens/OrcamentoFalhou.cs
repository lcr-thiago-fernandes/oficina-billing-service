using MassTransit;

namespace Oficina.Billing.Aplicacao.Mensagens;

[MessageUrn("oficina:saga:OrcamentoFalhou")]
[EntityName("oficina-saga-OrcamentoFalhou")]
public sealed record OrcamentoFalhou : IMensagemSaga
{
    public Guid OrdemServicoId { get; init; }
    public DateTime OcorridoEm { get; init; }
    public string Motivo { get; init; } = null!;
}
