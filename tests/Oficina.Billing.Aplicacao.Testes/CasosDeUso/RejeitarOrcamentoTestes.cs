using Moq;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Comum;
using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Aplicacao.Testes.CasosDeUso;

public class RejeitarOrcamentoTestes
{
    private readonly Mock<IRepositorioBilling> _repo = new();
    private readonly Mock<IPublicadorMensagens> _pub = new();
    private readonly Mock<ITelemetriaPagamento> _tel = new();

    private RejeitarOrcamentoCasoDeUso Criar(Orcamento o)
    {
        _repo.Setup(r => r.ObterOrcamentoAsync(Cenario.OsId, It.IsAny<CancellationToken>())).ReturnsAsync(o);
        return new(_repo.Object, _pub.Object, _tel.Object, new RelogioFixo());
    }

    [Fact]
    public async Task Rejeitar_Gerado_PublicaOrcamentoRejeitadoComMotivo()
    {
        var o = Cenario.Orcamento();

        var r = await Criar(o).ExecutarAsync(Cenario.OsId, Cenario.Dono, "caro", default);

        _repo.Verify(x => x.AtualizarOrcamentoAsync(It.Is<Orcamento>(a => a.Status == StatusOrcamento.Rejeitado), It.IsAny<CancellationToken>()), Times.Once);
        _pub.Verify(p => p.PublicarAsync(It.Is<OrcamentoRejeitado>(m => m.Motivo == "caro" && m.OrcamentoId == o.Id), It.IsAny<CancellationToken>()), Times.Once);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "OrcamentoRejeitado")), Times.Once);
        r.Status.Should().Be("Rejeitado");
        r.Motivo.Should().Be("caro");
    }

    [Fact]
    public async Task Rejeitar_JaRejeitado_Republica()
    {
        await Criar(Cenario.Orcamento(StatusOrcamento.Rejeitado)).ExecutarAsync(Cenario.OsId, Cenario.Dono, "x", default);

        _repo.Verify(x => x.AtualizarOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _pub.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoRejeitado>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejeitar_Aprovado_LancaTransicaoInvalida()
    {
        var act = () => Criar(Cenario.Orcamento(StatusOrcamento.Aprovado, "l")).ExecutarAsync(Cenario.OsId, Cenario.Dono, null, default);

        await act.Should().ThrowAsync<TransicaoInvalidaException>();
        _pub.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rejeitar_ClienteNaoDono_LancaNaoEncontrado()
    {
        var act = () => Criar(Cenario.Orcamento()).ExecutarAsync(Cenario.OsId, Cenario.Outro, null, default);

        await act.Should().ThrowAsync<OrcamentoNaoEncontradoException>();
    }
}
