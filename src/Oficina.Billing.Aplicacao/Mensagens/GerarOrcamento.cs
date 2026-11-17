using MassTransit;

namespace Oficina.Billing.Aplicacao.Mensagens;

[MessageUrn("oficina:saga:GerarOrcamento")]
[EntityName("oficina-saga-GerarOrcamento")]
public sealed record GerarOrcamento : IMensagemSaga
{
    public Guid OrdemServicoId { get; init; }
    public DateTime OcorridoEm { get; init; }
    public int NumeroOs { get; init; }
    public string ClienteDocumento { get; init; } = null!;
    public string ClienteNome { get; init; } = null!;
    public string? ClienteEmail { get; init; }
    public IReadOnlyList<ItemGerarOrcamento> Itens { get; init; } = null!;
}
