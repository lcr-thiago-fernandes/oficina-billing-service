using MassTransit;

namespace Oficina.Billing.Aplicacao.Mensagens;

[MessageUrn("oficina:saga:OrcamentoAprovado")]
[EntityName("oficina-saga-OrcamentoAprovado")]
public sealed record OrcamentoAprovado : IMensagemSaga
{
    public Guid OrdemServicoId { get; init; }
    public DateTime OcorridoEm { get; init; }
    public Guid OrcamentoId { get; init; }
    public string LinkPagamento { get; init; } = null!;
}
