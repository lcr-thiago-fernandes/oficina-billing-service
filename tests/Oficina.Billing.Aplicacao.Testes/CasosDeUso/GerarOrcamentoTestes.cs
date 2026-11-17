using Moq;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Aplicacao.Testes.CasosDeUso;

public class GerarOrcamentoTestes
{
    private readonly Mock<IRepositorioBilling> _repo = new();
    private readonly Mock<IPublicadorMensagens> _pub = new();
    private readonly Mock<ITelemetriaPagamento> _tel = new();

    private GerarOrcamentoCasoDeUso Criar() => new(_repo.Object, _pub.Object, _tel.Object, new RelogioFixo());

    [Fact]
    public async Task Gerar_Valido_InsereEPublicaOrcamentoGerado()
    {
        Orcamento? inserido = null;
        _repo.Setup(r => r.InserirOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()))
            .Callback<Orcamento, CancellationToken>((o, _) => inserido = o).ReturnsAsync(true);

        await Criar().ExecutarAsync(Cenario.Comando(), default);

        inserido!.Cliente.Documento.Should().Be(Cenario.Cpf);
        _pub.Verify(p => p.PublicarAsync(It.Is<OrcamentoGerado>(m =>
            m.OrdemServicoId == Cenario.OsId && m.ValorTotal == 191.00m && m.OrcamentoId == inserido.Id &&
            m.OcorridoEm == RelogioFixo.Agora), It.IsAny<CancellationToken>()), Times.Once);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "OrcamentoGerado" && e.Resultado == "Sucesso")), Times.Once);
    }

    [Fact]
    public async Task Gerar_SemItens_PublicaOrcamentoFalhouSemInserir()
    {
        await Criar().ExecutarAsync(Cenario.Comando(Array.Empty<ItemGerarOrcamento>()), default);

        _repo.Verify(r => r.InserirOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _pub.Verify(p => p.PublicarAsync(It.Is<OrcamentoFalhou>(m =>
            m.Motivo == "Orcamento sem itens" && m.OrdemServicoId == Cenario.OsId), It.IsAny<CancellationToken>()), Times.Once);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "OrcamentoFalhou" && e.Resultado == "Falha")), Times.Once);
    }

    [Fact]
    public async Task Gerar_Duplicado_RepublicaOrcamentoGeradoDoExistente()
    {
        var existente = Cenario.Orcamento();
        _repo.Setup(r => r.InserirOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.ObterOrcamentoAsync(Cenario.OsId, It.IsAny<CancellationToken>())).ReturnsAsync(existente);

        await Criar().ExecutarAsync(Cenario.Comando(), default);

        _pub.Verify(p => p.PublicarAsync(It.Is<OrcamentoGerado>(m => m.OrcamentoId == existente.Id && m.ValorTotal == 191.00m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Gerar_DuplicadoJaRejeitado_NaoPublica()
    {
        _repo.Setup(r => r.InserirOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.ObterOrcamentoAsync(Cenario.OsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Cenario.Orcamento(StatusOrcamento.Rejeitado));

        await Criar().ExecutarAsync(Cenario.Comando(), default);

        _pub.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoGerado>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
