namespace Oficina.Billing.Dominio.Orcamentos;

public record ItemOrcamento(TipoItem Tipo, Guid ReferenciaId, string Descricao, int Quantidade, decimal PrecoUnitario)
{
    public decimal Subtotal => Quantidade * PrecoUnitario;
}
