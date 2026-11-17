namespace Oficina.Billing.Aplicacao.Mensagens;

public sealed record ItemGerarOrcamento
{
    public TipoItemOrcamento Tipo { get; init; }
    public Guid ReferenciaId { get; init; }
    public string Descricao { get; init; } = null!;
    public int Quantidade { get; init; }
    public decimal PrecoUnitario { get; init; }
}
