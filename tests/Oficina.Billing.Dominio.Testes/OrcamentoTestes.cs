using Oficina.Billing.Dominio.Comum;
using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Dominio.Testes;

public class OrcamentoTestes
{
    private static readonly DateTime Agora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DadosCliente Cliente = new("529.982.247-25", "Maria", "maria@x.com");

    private static ItemOrcamento Item(int q = 1, decimal p = 10m) =>
        new(TipoItem.Servico, Guid.NewGuid(), "Troca de oleo", q, p);

    private static Orcamento Novo() => Orcamento.Gerar(Guid.NewGuid(), 7, Cliente, new[] { Item() }, Agora);

    private static Orcamento ComStatus(StatusOrcamento s)
    {
        var o = Novo();
        return Orcamento.Reconstituir(o.Id, o.OrdemServicoId, o.NumeroOs, o.Cliente, o.Itens, o.ValorTotal, s,
            null, null, null, o.CriadoEm, o.AtualizadoEm, 0);
    }

    [Fact]
    public void Gerar_CalculaValorTotal()
    {
        var o = Orcamento.Gerar(Guid.NewGuid(), 1, Cliente,
            new[] { Item(1, 120.00m), Item(2, 35.50m) }, Agora);
        o.ValorTotal.Should().Be(191.00m);
        o.Status.Should().Be(StatusOrcamento.Gerado);
        o.Versao.Should().Be(0);
        o.Id.Should().NotBe(Guid.Empty);
        o.Cliente.Documento.Should().Be("52998224725");
        o.CriadoEm.Should().Be(Agora);
    }

    [Fact]
    public void Gerar_SemItens_LancaOrcamentoInvalido()
    {
        var act = () => Orcamento.Gerar(Guid.NewGuid(), 1, Cliente, Array.Empty<ItemOrcamento>(), Agora);
        act.Should().Throw<OrcamentoInvalidoException>().Which.Motivo.Should().Be("Orcamento sem itens");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Gerar_QuantidadeNaoPositiva_Lanca(int q)
    {
        var act = () => Orcamento.Gerar(Guid.NewGuid(), 1, Cliente, new[] { Item(q) }, Agora);
        act.Should().Throw<OrcamentoInvalidoException>();
    }

    [Theory]
    [InlineData("-0.01", "Preco unitario deve ser positivo")]
    [InlineData("0", "Preco unitario deve ser positivo")] // MP rejeita unit_price 0
    [InlineData("10.005", "Preco unitario com mais de 2 casas decimais")] // arredondaria diferente do MP
    public void Gerar_PrecoInvalido_Lanca(string preco, string motivo)
    {
        var p = decimal.Parse(preco, System.Globalization.CultureInfo.InvariantCulture);
        var act = () => Orcamento.Gerar(Guid.NewGuid(), 1, Cliente, new[] { Item(1, p) }, Agora);
        act.Should().Throw<OrcamentoInvalidoException>().Which.Motivo.Should().Be(motivo);
    }

    [Fact]
    public void Gerar_PrecoComZerosAMais_Aceita() =>
        Orcamento.Gerar(Guid.NewGuid(), 1, Cliente, new[] { Item(1, 10.5000m) }, Agora).ValorTotal.Should().Be(10.50m);

    [Fact]
    public void Gerar_DocumentoVazio_Lanca()
    {
        var act = () => Orcamento.Gerar(Guid.NewGuid(), 1, new DadosCliente("  ", "Maria", null), new[] { Item() }, Agora);
        act.Should().Throw<OrcamentoInvalidoException>();
    }

    [Fact]
    public void Gerar_OrdemServicoIdVazio_Lanca()
    {
        var act = () => Orcamento.Gerar(Guid.Empty, 1, Cliente, new[] { Item() }, Agora);
        act.Should().Throw<OrcamentoInvalidoException>();
    }

    [Fact]
    public void Aprovar_DeGerado_GuardaLinkEPreferencia()
    {
        var o = Novo();
        var depois = Agora.AddMinutes(1);
        o.Aprovar("pref-1", "https://pagar/1", depois);
        o.Status.Should().Be(StatusOrcamento.Aprovado);
        o.PreferenciaId.Should().Be("pref-1");
        o.LinkPagamento.Should().Be("https://pagar/1");
        o.AtualizadoEm.Should().Be(depois);
        o.Versao.Should().Be(0);
    }

    [Theory]
    [InlineData(StatusOrcamento.Rejeitado)]
    [InlineData(StatusOrcamento.Cancelado)]
    [InlineData(StatusOrcamento.Aprovado)]
    public void Aprovar_ForaDeGerado_LancaTransicaoInvalida(StatusOrcamento s)
    {
        var o = ComStatus(s);
        var act = () => o.Aprovar("p", "l", Agora);
        act.Should().Throw<TransicaoInvalidaException>();
    }

    [Fact]
    public void Rejeitar_DeGerado_GuardaMotivo()
    {
        var o = Novo();
        o.Rejeitar("sem verba", Agora.AddMinutes(1));
        o.Status.Should().Be(StatusOrcamento.Rejeitado);
        o.Motivo.Should().Be("sem verba");
    }

    [Fact]
    public void Rejeitar_DeAprovado_LancaTransicaoInvalida()
    {
        var o = ComStatus(StatusOrcamento.Aprovado);
        var act = () => o.Rejeitar("x", Agora);
        act.Should().Throw<TransicaoInvalidaException>();
    }

    [Theory]
    [InlineData(StatusOrcamento.Gerado)]
    [InlineData(StatusOrcamento.Aprovado)]
    public void Cancelar_DeGeradoOuAprovado_RetornaTrue(StatusOrcamento s)
    {
        var o = ComStatus(s);
        o.Cancelar("falha", Agora.AddMinutes(1)).Should().BeTrue();
        o.Status.Should().Be(StatusOrcamento.Cancelado);
        o.Motivo.Should().Be("falha");
    }

    [Theory]
    [InlineData(StatusOrcamento.Cancelado)]
    [InlineData(StatusOrcamento.Rejeitado)]
    public void Cancelar_JaTerminal_RetornaFalseSemAlterar(StatusOrcamento s)
    {
        var o = ComStatus(s);
        var antes = o.AtualizadoEm;
        o.Cancelar("falha", Agora.AddMinutes(5)).Should().BeFalse();
        o.Status.Should().Be(s);
        o.Motivo.Should().BeNull();
        o.AtualizadoEm.Should().Be(antes);
    }

    [Fact]
    public void IncrementarVersao_SomaUm()
    {
        var o = Novo();
        o.IncrementarVersao();
        o.Versao.Should().Be(1);
    }
}
