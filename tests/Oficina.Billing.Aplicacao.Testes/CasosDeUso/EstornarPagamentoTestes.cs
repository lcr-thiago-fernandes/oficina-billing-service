using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Aplicacao.Testes.CasosDeUso;

public class EstornarPagamentoTestes
{
    private const string Id = "1325123456";

    private readonly Mock<IRepositorioBilling> _repo = new();
    private readonly Mock<IGatewayPagamento> _gw = new();
    private readonly Mock<IPublicadorMensagens> _pub = new();
    private readonly Mock<ITelemetriaPagamento> _tel = new();

    private static readonly EstornarPagamento Comando = new()
    {
        OrdemServicoId = Cenario.OsId,
        OcorridoEm = RelogioFixo.Agora,
        Motivo = "execucao_falhou",
    };

    public EstornarPagamentoTestes()
    {
        _gw.SetupGet(g => g.Nome).Returns("Simulado");
        _repo.Setup(r => r.ListarPagamentosAsync(Cenario.OsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Pagamento>());
    }

    private EstornarPagamentoCasoDeUso Criar() =>
        new(_repo.Object, _gw.Object, _pub.Object, _tel.Object, new RelogioFixo(),
            NullLogger<EstornarPagamentoCasoDeUso>.Instance);

    private void EstornoNoGateway() =>
        _gw.Setup(g => g.EstornarAsync(Id, 191.00m, "estorno-1325123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EstornoGateway("est-9", 191.00m));

    private void ConflitoAoSalvar() =>
        _repo.Setup(x => x.SalvarPagamentoAsync(It.IsAny<Pagamento>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcorrenciaException("conflito"));

    [Fact]
    public async Task GatewayRecusaEstorno4xx_PublicaEstornoFalhouSemEsperarRetry()
    {
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamento(StatusPagamento.Aprovado);
        _gw.Setup(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("MP 400"));

        await Criar().ExecutarAsync(Comando, ultimaTentativa: false, default);

        _pub.Verify(p => p.PublicarAsync(It.Is<EstornoFalhou>(m => m.Motivo == "Mercado Pago recusou o estorno"),
            It.IsAny<CancellationToken>()), Times.Once);
        _pub.Verify(p => p.PublicarAsync(It.IsAny<PagamentoEstornado>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(x => x.SalvarPagamentoAsync(It.IsAny<Pagamento>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "EstornoFalhou" && e.Resultado == "Falha")), Times.Once);
    }

    [Fact]
    public async Task ConflitoAoGravar_UltimaTentativa_PublicaEstornoFalhou()
    {
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamento(StatusPagamento.Aprovado);
        EstornoNoGateway();
        ConflitoAoSalvar();

        await Criar().ExecutarAsync(Comando, ultimaTentativa: true, default);

        _pub.Verify(p => p.PublicarAsync(It.Is<EstornoFalhou>(m => m.Motivo == "Falha ao processar o estorno apos 3 tentativas"),
            It.IsAny<CancellationToken>()), Times.Once);
        _pub.Verify(p => p.PublicarAsync(It.IsAny<PagamentoEstornado>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConflitoAoGravar_NaoUltima_Relanca()
    {
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamento(StatusPagamento.Aprovado);
        EstornoNoGateway();
        ConflitoAoSalvar();

        var act = () => Criar().ExecutarAsync(Comando, ultimaTentativa: false, default);

        await act.Should().ThrowAsync<ConcorrenciaException>();
        _pub.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancelamento_UltimaTentativa_Relanca()
    {
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamento(StatusPagamento.Aprovado);
        _gw.Setup(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => Criar().ExecutarAsync(Comando, ultimaTentativa: true, default);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _pub.VerifyNoOtherCalls();
    }

    private Orcamento ComOrcamento(StatusOrcamento status)
    {
        var o = Cenario.Orcamento(status, status == StatusOrcamento.Aprovado ? "https://pagar/1" : null);
        _repo.Setup(r => r.ObterOrcamentoAsync(Cenario.OsId, It.IsAny<CancellationToken>())).ReturnsAsync(o);
        return o;
    }

    private Pagamento ComPagamento(StatusPagamento status)
    {
        var p = Pagamento.Registrar(Id, Cenario.OsId, 191.00m, "pix", StatusPagamento.Aprovado, null, RelogioFixo.Agora);
        if (status == StatusPagamento.Estornado)
            p.Estornar("est-0", RelogioFixo.Agora);
        _repo.Setup(r => r.ListarPagamentosAsync(Cenario.OsId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { p });
        return p;
    }

    private void GatewayFora() =>
        _gw.Setup(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GatewayPagamentoIndisponivelException("fora"));

    [Fact]
    public async Task Aprovado_EstornaComChaveIdempotenteEPublica()
    {
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamento(StatusPagamento.Aprovado);
        string? chave = null;
        _gw.Setup(g => g.EstornarAsync(Id, 191.00m, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, decimal, string, CancellationToken>((_, _, c, _) => chave = c)
            .ReturnsAsync(new EstornoGateway("est-9", 191.00m));

        await Criar().ExecutarAsync(Comando, ultimaTentativa: false, default);

        chave.Should().Be("estorno-1325123456");
        _repo.Verify(x => x.SalvarPagamentoAsync(It.Is<Pagamento>(p => p.Status == StatusPagamento.Estornado &&
            p.EstornoId == "est-9"), false, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(x => x.AtualizarOrcamentoAsync(It.Is<Orcamento>(o => o.Status == StatusOrcamento.Cancelado &&
            o.Motivo == "execucao_falhou"), It.IsAny<CancellationToken>()), Times.Once);
        _pub.Verify(p => p.PublicarAsync(It.Is<PagamentoEstornado>(m => m.PagamentoId == Id && m.Valor == 191.00m &&
            m.OrdemServicoId == Cenario.OsId), It.IsAny<CancellationToken>()), Times.Once);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "PagamentoEstornado" && e.Resultado == "Sucesso")), Times.Once);
    }

    [Fact]
    public async Task Aprovado_OrcamentoJaCancelado_NaoAtualizaOrcamento()
    {
        ComOrcamento(StatusOrcamento.Cancelado);
        ComPagamento(StatusPagamento.Aprovado);
        _gw.Setup(g => g.EstornarAsync(Id, 191.00m, "estorno-1325123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EstornoGateway("est-9", 191.00m));

        await Criar().ExecutarAsync(Comando, ultimaTentativa: false, default);

        _repo.Verify(x => x.AtualizarOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _pub.Verify(p => p.PublicarAsync(It.IsAny<PagamentoEstornado>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JaEstornado_RepublicaSemChamarGateway()
    {
        ComOrcamento(StatusOrcamento.Cancelado);
        ComPagamento(StatusPagamento.Estornado);

        await Criar().ExecutarAsync(Comando, ultimaTentativa: false, default);

        _gw.Verify(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(x => x.SalvarPagamentoAsync(It.IsAny<Pagamento>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _pub.Verify(p => p.PublicarAsync(It.Is<PagamentoEstornado>(m => m.PagamentoId == Id && m.Valor == 191.00m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JaEstornado_OrcamentoAindaAprovado_CompletaCancelamento()
    {
        // Queda entre gravar o estorno e cancelar o orcamento: a reentrega conclui a compensacao.
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamento(StatusPagamento.Estornado);

        await Criar().ExecutarAsync(Comando, ultimaTentativa: false, default);

        _repo.Verify(x => x.AtualizarOrcamentoAsync(It.Is<Orcamento>(o => o.Status == StatusOrcamento.Cancelado),
            It.IsAny<CancellationToken>()), Times.Once);
        _pub.Verify(p => p.PublicarAsync(It.IsAny<PagamentoEstornado>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SemPagamentoAprovado_PublicaEstornoFalhou()
    {
        ComOrcamento(StatusOrcamento.Aprovado);

        await Criar().ExecutarAsync(Comando, ultimaTentativa: false, default);

        _pub.Verify(p => p.PublicarAsync(It.Is<EstornoFalhou>(m => m.Motivo == "Nenhum pagamento aprovado para a OS" &&
            m.OrdemServicoId == Cenario.OsId), It.IsAny<CancellationToken>()), Times.Once);
        _gw.Verify(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "EstornoFalhou" && e.Resultado == "Falha")), Times.Once);
    }

    [Fact]
    public async Task GatewayIndisponivel_UltimaTentativa_PublicaEstornoFalhou()
    {
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamento(StatusPagamento.Aprovado);
        GatewayFora();

        await Criar().ExecutarAsync(Comando, ultimaTentativa: true, default);

        _pub.Verify(p => p.PublicarAsync(It.Is<EstornoFalhou>(m => m.Motivo == "Mercado Pago indisponivel apos 3 tentativas"),
            It.IsAny<CancellationToken>()), Times.Once);
        _pub.Verify(p => p.PublicarAsync(It.IsAny<PagamentoEstornado>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(x => x.SalvarPagamentoAsync(It.IsAny<Pagamento>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "EstornoFalhou" && e.Resultado == "Falha")), Times.Once);
    }

    [Fact]
    public async Task GatewayIndisponivel_NaoUltima_Relanca()
    {
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamento(StatusPagamento.Aprovado);
        GatewayFora();

        var act = () => Criar().ExecutarAsync(Comando, ultimaTentativa: false, default);

        await act.Should().ThrowAsync<GatewayPagamentoIndisponivelException>();
        _pub.VerifyNoOtherCalls();
        _repo.Verify(x => x.SalvarPagamentoAsync(It.IsAny<Pagamento>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
