using Moq;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Comum;
using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Aplicacao.Testes.CasosDeUso;

public class AprovarOrcamentoTestes
{
    private readonly Mock<IRepositorioBilling> _repo = new();
    private readonly Mock<IGatewayPagamento> _gw = new();
    private readonly Mock<IPublicadorMensagens> _pub = new();
    private readonly Mock<ITelemetriaPagamento> _tel = new();

    public AprovarOrcamentoTestes() => _gw.SetupGet(g => g.Nome).Returns("Simulado");

    private AprovarOrcamentoCasoDeUso Criar() =>
        new(_repo.Object, _gw.Object, _pub.Object, _tel.Object, new RelogioFixo());

    private Orcamento Existente(Orcamento o)
    {
        _repo.Setup(r => r.ObterOrcamentoAsync(Cenario.OsId, It.IsAny<CancellationToken>())).ReturnsAsync(o);
        return o;
    }

    [Fact]
    public async Task Aprovar_Gerado_CriaCobrancaAtualizaEPublicaOrcamentoAprovado()
    {
        var o = Existente(Cenario.Orcamento());
        NovaCobranca? cobranca = null;
        _gw.Setup(g => g.CriarCobrancaAsync(It.IsAny<NovaCobranca>(), It.IsAny<CancellationToken>()))
            .Callback<NovaCobranca, CancellationToken>((c, _) => cobranca = c)
            .ReturnsAsync(new CobrancaCriada("pref-9", "https://pagar/9"));

        var r = await Criar().ExecutarAsync(Cenario.OsId, Cenario.Dono, default);

        cobranca!.Itens.Select(i => i.Titulo).Should().Equal("Troca de oleo", "Filtro");
        cobranca.NumeroOs.Should().Be(7);
        cobranca.EmailPagador.Should().Be("m@x.com");
        _repo.Verify(x => x.AtualizarOrcamentoAsync(It.Is<Orcamento>(a => a.Status == StatusOrcamento.Aprovado), It.IsAny<CancellationToken>()), Times.Once);
        _pub.Verify(p => p.PublicarAsync(It.Is<OrcamentoAprovado>(m =>
            m.LinkPagamento == "https://pagar/9" && m.OrcamentoId == o.Id && m.OrdemServicoId == Cenario.OsId),
            It.IsAny<CancellationToken>()), Times.Once);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "OrcamentoAprovado" && e.Gateway == "Simulado")), Times.Once);
        r.Status.Should().Be("Aprovado");
        r.LinkPagamento.Should().Be("https://pagar/9");
    }

    [Fact]
    public async Task Aprovar_JaAprovado_RepublicaSemNovaCobranca()
    {
        Existente(Cenario.Orcamento(StatusOrcamento.Aprovado, "https://pagar/1"));

        var r = await Criar().ExecutarAsync(Cenario.OsId, Cenario.Dono, default);

        _gw.Verify(g => g.CriarCobrancaAsync(It.IsAny<NovaCobranca>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(x => x.AtualizarOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _pub.Verify(p => p.PublicarAsync(It.Is<OrcamentoAprovado>(m => m.LinkPagamento == "https://pagar/1"), It.IsAny<CancellationToken>()), Times.Once);
        r.Status.Should().Be("Aprovado");
    }

    [Fact]
    public async Task Aprovar_Rejeitado_LancaTransicaoInvalida()
    {
        Existente(Cenario.Orcamento(StatusOrcamento.Rejeitado));

        var act = () => Criar().ExecutarAsync(Cenario.OsId, Cenario.Dono, default);

        await act.Should().ThrowAsync<TransicaoInvalidaException>();
        _gw.Verify(g => g.CriarCobrancaAsync(It.IsAny<NovaCobranca>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Aprovar_GatewayIndisponivel_PropagaENaoAtualiza()
    {
        Existente(Cenario.Orcamento());
        _gw.Setup(g => g.CriarCobrancaAsync(It.IsAny<NovaCobranca>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GatewayPagamentoIndisponivelException("fora"));

        var act = () => Criar().ExecutarAsync(Cenario.OsId, Cenario.Dono, default);

        await act.Should().ThrowAsync<GatewayPagamentoIndisponivelException>();
        _repo.Verify(x => x.AtualizarOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _pub.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Aprovar_ConflitoDeVersao_PropagaConcorrencia()
    {
        Existente(Cenario.Orcamento());
        _gw.Setup(g => g.CriarCobrancaAsync(It.IsAny<NovaCobranca>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CobrancaCriada("p", "l"));
        _repo.Setup(x => x.AtualizarOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcorrenciaException("conflito"));

        var act = () => Criar().ExecutarAsync(Cenario.OsId, Cenario.Dono, default);

        await act.Should().ThrowAsync<ConcorrenciaException>();
        _pub.VerifyNoOtherCalls();
        _tel.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Aprovar_ClienteNaoDono_LancaNaoEncontrado()
    {
        Existente(Cenario.Orcamento());

        var act = () => Criar().ExecutarAsync(Cenario.OsId, Cenario.Outro, default);

        await act.Should().ThrowAsync<OrcamentoNaoEncontradoException>();
        _gw.Verify(g => g.CriarCobrancaAsync(It.IsAny<NovaCobranca>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
