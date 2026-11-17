using MassTransit;

namespace Oficina.Billing.Aplicacao.Mensagens;

[MessageUrn("oficina:saga:EstornarPagamento")]
[EntityName("oficina-saga-EstornarPagamento")]
public sealed record EstornarPagamento : IMensagemSaga
{
    public Guid OrdemServicoId { get; init; }
    public DateTime OcorridoEm { get; init; }
    public string Motivo { get; init; } = null!;
}
