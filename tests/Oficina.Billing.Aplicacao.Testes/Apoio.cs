using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Aplicacao.Testes;

internal sealed class RelogioFixo : TimeProvider
{
    public static readonly DateTime Agora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    public override DateTimeOffset GetUtcNow() => new(Agora);
}

internal static class Cenario
{
    public const string Cpf = "52998224725";
    public static readonly Guid OsId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static Orcamento Orcamento(StatusOrcamento status = StatusOrcamento.Gerado, string? link = null)
    {
        var o = Dominio.Orcamentos.Orcamento.Gerar(OsId, 7, new DadosCliente(Cpf, "Maria", "m@x.com"),
            new[]
            {
                new ItemOrcamento(TipoItem.Servico, Guid.NewGuid(), "Troca de oleo", 1, 91.00m),
                new ItemOrcamento(TipoItem.Peca, Guid.NewGuid(), "Filtro", 2, 50.00m),
            }, RelogioFixo.Agora);
        return Dominio.Orcamentos.Orcamento.Reconstituir(o.Id, o.OrdemServicoId, o.NumeroOs, o.Cliente, o.Itens,
            o.ValorTotal, status, null, link is null ? null : "pref-1", link, o.CriadoEm, o.AtualizadoEm, 0);
    }

    public static GerarOrcamento Comando(ItemGerarOrcamento[]? itens = null) => new()
    {
        OrdemServicoId = OsId, NumeroOs = 7, ClienteDocumento = "529.982.247-25", ClienteNome = "Maria",
        ClienteEmail = "m@x.com", OcorridoEm = RelogioFixo.Agora,
        Itens = itens ?? new[]
        {
            new ItemGerarOrcamento { Tipo = TipoItemOrcamento.Servico, ReferenciaId = Guid.NewGuid(), Descricao = "Troca de oleo", Quantidade = 1, PrecoUnitario = 91.00m },
            new ItemGerarOrcamento { Tipo = TipoItemOrcamento.Peca, ReferenciaId = Guid.NewGuid(), Descricao = "Filtro", Quantidade = 2, PrecoUnitario = 50.00m },
        },
    };

    public static readonly Solicitante Dono = new("Cliente", "529.982.247-25");
    public static readonly Solicitante Outro = new("Cliente", "111.444.777-35");
    public static readonly Solicitante Atendente = new("Atendente", null);
}
