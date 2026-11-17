using MassTransit;

namespace Oficina.Billing.Aplicacao.Mensagens;

[MessageUrn("oficina:saga:OrcamentoGerado")]
[EntityName("oficina-saga-OrcamentoGerado")]
public sealed record OrcamentoGerado : IMensagemSaga
{
    public Guid OrdemServicoId { get; init; }
    public DateTime OcorridoEm { get; init; }
    public Guid OrcamentoId { get; init; }
    public decimal ValorTotal { get; init; }
}
