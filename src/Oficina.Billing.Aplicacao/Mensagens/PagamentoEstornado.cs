using MassTransit;

namespace Oficina.Billing.Aplicacao.Mensagens;

[MessageUrn("oficina:saga:PagamentoEstornado")]
[EntityName("oficina-saga-PagamentoEstornado")]
public sealed record PagamentoEstornado : IMensagemSaga
{
    public Guid OrdemServicoId { get; init; }
    public DateTime OcorridoEm { get; init; }
    public string PagamentoId { get; init; } = null!;
    public decimal Valor { get; init; }
}
