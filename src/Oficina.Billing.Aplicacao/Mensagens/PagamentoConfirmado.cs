using MassTransit;

namespace Oficina.Billing.Aplicacao.Mensagens;

[MessageUrn("oficina:saga:PagamentoConfirmado")]
[EntityName("oficina-saga-PagamentoConfirmado")]
public sealed record PagamentoConfirmado : IMensagemSaga
{
    public Guid OrdemServicoId { get; init; }
    public DateTime OcorridoEm { get; init; }
    public string PagamentoId { get; init; } = null!;
    public decimal Valor { get; init; }
    public string Meio { get; init; } = null!;
}
