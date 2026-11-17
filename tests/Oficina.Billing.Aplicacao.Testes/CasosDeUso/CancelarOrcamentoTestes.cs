using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Aplicacao.Testes.CasosDeUso;

public class CancelarOrcamentoTestes
{
    private readonly Mock<IRepositorioBilling> _repo = new();
    private readonly Mock<ITelemetriaPagamento> _tel = new();

    private CancelarOrcamentoCasoDeUso Criar(Orcamento? o)
    {
        _repo.Setup(r => r.ObterOrcamentoAsync(Cenario.OsId, It.IsAny<CancellationToken>())).ReturnsAsync(o);
        return new(_repo.Object, _tel.Object, new RelogioFixo(), NullLogger<CancelarOrcamentoCasoDeUso>.Instance);
    }

    private static CancelarOrcamento Cmd => new() { OrdemServicoId = Cenario.OsId, Motivo = "OS cancelada", OcorridoEm = RelogioFixo.Agora };

    [Fact]
    public async Task Cancelar_Aprovado_AtualizaParaCancelado()
    {
        await Criar(Cenario.Orcamento(StatusOrcamento.Aprovado, "l")).ExecutarAsync(Cmd, default);

        _repo.Verify(x => x.AtualizarOrcamentoAsync(It.Is<Orcamento>(a =>
            a.Status == StatusOrcamento.Cancelado && a.Motivo == "OS cancelada"), It.IsAny<CancellationToken>()), Times.Once);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "OrcamentoCancelado" && e.Resultado == "Sucesso")), Times.Once);
    }

    [Fact]
    public async Task Cancelar_JaCancelado_NaoAtualiza()
    {
        await Criar(Cenario.Orcamento(StatusOrcamento.Cancelado)).ExecutarAsync(Cmd, default);

        _repo.Verify(x => x.AtualizarOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _tel.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancelar_Inexistente_NaoLanca()
    {
        var act = () => Criar(null).ExecutarAsync(Cmd, default);

        await act.Should().NotThrowAsync();
        _repo.Verify(x => x.AtualizarOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
