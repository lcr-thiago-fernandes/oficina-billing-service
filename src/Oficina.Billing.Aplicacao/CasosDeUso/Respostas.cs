using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

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

public record PagamentoResposta(string PagamentoId, string Status, decimal Valor, string Meio, string? DetalheStatus,
    string? EstornoId, DateTime CriadoEm, DateTime AtualizadoEm)
{
    public static PagamentoResposta De(Pagamento p) => new(p.Id, p.Status.ToString(), p.Valor, p.Meio,
        p.DetalheStatus, p.EstornoId, p.CriadoEm, p.AtualizadoEm);
}
