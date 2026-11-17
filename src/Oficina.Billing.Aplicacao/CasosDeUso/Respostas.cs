using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Aplicacao.CasosDeUso;

public record ItemOrcamentoResposta(string Tipo, string Descricao, int Quantidade, decimal PrecoUnitario, decimal Subtotal);

public record OrcamentoResposta(Guid OrcamentoId, Guid OrdemServicoId, int NumeroOs, string Status, decimal ValorTotal,
    IReadOnlyList<ItemOrcamentoResposta> Itens, string? LinkPagamento, string? Motivo, DateTime CriadoEm,
    DateTime AtualizadoEm)
{
    public static OrcamentoResposta De(Orcamento o) => new(o.Id, o.OrdemServicoId, o.NumeroOs, o.Status.ToString(),
        o.ValorTotal,
        o.Itens.Select(i => new ItemOrcamentoResposta(i.Tipo.ToString(), i.Descricao, i.Quantidade, i.PrecoUnitario,
            i.Subtotal)).ToList(),
        o.LinkPagamento, o.Motivo, o.CriadoEm, o.AtualizadoEm);
}
