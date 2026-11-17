using Oficina.Billing.Dominio.Comum;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Dominio.Testes;

public class PagamentoTestes
{
    private static readonly DateTime Agora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static Pagamento Novo(StatusPagamento s) =>
        Pagamento.Registrar("123", Guid.NewGuid(), 50m, "pix", s, null, Agora);

    [Fact]
    public void Pagamento_Registrar_ComEstornado_Lanca()
    {
        var act = () => Novo(StatusPagamento.Estornado);
        act.Should().Throw<DominioException>();
    }

    [Theory]
    [InlineData(StatusPagamento.Pendente)]
    [InlineData(StatusPagamento.Aprovado)]
    [InlineData(StatusPagamento.Recusado)]
    public void Pagamento_Registrar_StatusValidos(StatusPagamento s)
    {
        var p = Novo(s);
        p.Status.Should().Be(s);
        p.Versao.Should().Be(0);
        p.CriadoEm.Should().Be(Agora);
    }

    [Fact]
    public void Pagamento_PendenteParaAprovado_e_PendenteParaRecusado()
    {
        var a = Novo(StatusPagamento.Pendente);
        a.Aprovar(Agora.AddMinutes(1));
        a.Status.Should().Be(StatusPagamento.Aprovado);
        a.AtualizadoEm.Should().Be(Agora.AddMinutes(1));

        var r = Novo(StatusPagamento.Pendente);
        r.Recusar("cc_rejected", Agora.AddMinutes(1));
        r.Status.Should().Be(StatusPagamento.Recusado);
        r.DetalheStatus.Should().Be("cc_rejected");
    }

    [Fact]
    public void Pagamento_AprovadoParaEstornado_GuardaEstornoId()
    {
        var p = Novo(StatusPagamento.Aprovado);
        p.Estornar("est-1", Agora.AddMinutes(1));
        p.Status.Should().Be(StatusPagamento.Estornado);
        p.EstornoId.Should().Be("est-1");
    }

    [Fact]
    public void Pagamento_RecusadoParaEstornado_LancaTransicaoInvalida()
    {
        var p = Novo(StatusPagamento.Recusado);
        var act = () => p.Estornar("est-1", Agora);
        act.Should().Throw<TransicaoInvalidaException>();
    }

    [Fact]
    public void Pagamento_AprovarOuRecusarForaDePendente_Lanca()
    {
        var p = Novo(StatusPagamento.Aprovado);
        var aprovar = () => p.Aprovar(Agora);
        var recusar = () => p.Recusar(null, Agora);
        aprovar.Should().Throw<TransicaoInvalidaException>();
        recusar.Should().Throw<TransicaoInvalidaException>();
    }

    [Fact]
    public void IncrementarVersao_SomaUm()
    {
        var p = Novo(StatusPagamento.Pendente);
        p.IncrementarVersao();
        p.Versao.Should().Be(1);
    }
}
