using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Aplicacao.Testes.CasosDeUso;

public class ProcessarNotificacaoPagamentoTestes
{
    private const string Id = "1325123456";

    private readonly Mock<IRepositorioBilling> _repo = new();
    private readonly Mock<IGatewayPagamento> _gw = new();
    private readonly Mock<IPublicadorMensagens> _pub = new();
    private readonly Mock<ITelemetriaPagamento> _tel = new();
    private DesfechoPagamento? _desfecho;

    public ProcessarNotificacaoPagamentoTestes()
    {
        _gw.SetupGet(g => g.Nome).Returns("Simulado");
        _repo.Setup(r => r.ListarPagamentosAsync(Cenario.OsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Pagamento>());
        _repo.Setup(r => r.RegistrarDesfechoAsync(It.IsAny<DesfechoPagamento>(), It.IsAny<CancellationToken>()))
            .Callback<DesfechoPagamento, CancellationToken>((d, _) => _desfecho = d)
            .ReturnsAsync(true);
    }

    private ProcessarNotificacaoPagamentoCasoDeUso Criar() => new(_repo.Object, _gw.Object, _pub.Object, _tel.Object,
        new RelogioFixo(), NullLogger<ProcessarNotificacaoPagamentoCasoDeUso>.Instance);

    private void NoGateway(StatusPagamentoGateway status, decimal valor = 191.00m, string? detalhe = null,
        bool semOs = false) =>
        _gw.Setup(g => g.ObterPagamentoAsync(Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoGateway(Id, semOs ? null : Cenario.OsId, status, detalhe, valor, "pix"));

    private Orcamento ComOrcamento(StatusOrcamento status)
    {
        var o = Cenario.Orcamento(status, status == StatusOrcamento.Aprovado ? "https://pagar/1" : null);
        _repo.Setup(r => r.ObterOrcamentoAsync(Cenario.OsId, It.IsAny<CancellationToken>())).ReturnsAsync(o);
        return o;
    }

    private void ComPagamentoSalvo(StatusPagamento status) =>
        _repo.Setup(r => r.ListarPagamentosAsync(Cenario.OsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Pagamento.Registrar(Id, Cenario.OsId, 191.00m, "pix", status, null, RelogioFixo.Agora) });

    private void EstornoNoGateway() =>
        _gw.Setup(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EstornoGateway("est-1", 191.00m));

    private void NuncaEstornou() =>
        _gw.Verify(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

    private void NuncaRegistrou() =>
        _repo.Verify(x => x.RegistrarDesfechoAsync(It.IsAny<DesfechoPagamento>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Aprovado_ComOrcamentoAprovado_RegistraEPublicaConfirmado()
    {
        NoGateway(StatusPagamentoGateway.Aprovado);
        ComOrcamento(StatusOrcamento.Aprovado);

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Processado);
        _desfecho!.Pagamento.Status.Should().Be(StatusPagamento.Aprovado);
        _desfecho.PagamentoNovo.Should().BeTrue();
        _desfecho.OrcamentoAlterado.Should().BeNull();
        _desfecho.Webhook.EventoTipo.Should().Be("PagamentoConfirmado");
        _desfecho.Webhook.EventoPublicado.Should().BeFalse();
        _pub.Verify(p => p.PublicarAsync(It.Is<PagamentoConfirmado>(m => m.PagamentoId == "1325123456" &&
            m.Valor == 191.00m && m.Meio == "pix" && m.OrdemServicoId == Cenario.OsId), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(x => x.MarcarEventoPublicadoAsync(Id, It.IsAny<CancellationToken>()), Times.Once);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "PagamentoConfirmado" &&
            e.Resultado == "Sucesso" && e.Meio == "pix" && e.Gateway == "Simulado")), Times.Once);
        NuncaEstornou();
    }

    [Fact]
    public async Task Recusado_ComOrcamentoAprovado_CancelaOrcamentoEPublicaRecusado()
    {
        NoGateway(StatusPagamentoGateway.Recusado, detalhe: "cc_rejected_insufficient_amount");
        ComOrcamento(StatusOrcamento.Aprovado);

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Processado);
        _desfecho!.Pagamento.Status.Should().Be(StatusPagamento.Recusado);
        _desfecho.Pagamento.DetalheStatus.Should().Be("cc_rejected_insufficient_amount");
        _desfecho.OrcamentoAlterado!.Status.Should().Be(StatusOrcamento.Cancelado);
        _desfecho.Webhook.EventoTipo.Should().Be("PagamentoRecusado");
        _desfecho.Webhook.Motivo.Should().Be("cc_rejected_insufficient_amount");
        _pub.Verify(p => p.PublicarAsync(It.Is<PagamentoRecusado>(m => m.PagamentoId == Id &&
            m.Motivo == "cc_rejected_insufficient_amount"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(x => x.MarcarEventoPublicadoAsync(Id, It.IsAny<CancellationToken>()), Times.Once);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "PagamentoRecusado")), Times.Once);
    }

    [Fact]
    public async Task Recusado_SemDetalhe_UsaRejected()
    {
        NoGateway(StatusPagamentoGateway.Recusado);
        ComOrcamento(StatusOrcamento.Aprovado);

        await Criar().ExecutarAsync(Id, default);

        _desfecho!.OrcamentoAlterado!.Motivo.Should().Be("rejected");
        _pub.Verify(p => p.PublicarAsync(It.Is<PagamentoRecusado>(m => m.Motivo == "rejected"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recusado_OrcamentoCancelado_RegistraSemPublicar()
    {
        NoGateway(StatusPagamentoGateway.Recusado, detalhe: "cc_rejected_other_reason");
        ComOrcamento(StatusOrcamento.Cancelado);

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Processado);
        _desfecho!.Pagamento.Status.Should().Be(StatusPagamento.Recusado);
        _desfecho.OrcamentoAlterado.Should().BeNull();
        _desfecho.Webhook.EventoTipo.Should().Be("Nenhum");
        _pub.VerifyNoOtherCalls();
        _repo.Verify(x => x.MarcarEventoPublicadoAsync(Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Aprovado_OrcamentoCancelado_EstornaAutomaticamenteSemConfirmar()
    {
        NoGateway(StatusPagamentoGateway.Aprovado);
        ComOrcamento(StatusOrcamento.Cancelado);
        EstornoNoGateway();

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Processado);
        _gw.Verify(g => g.EstornarAsync(Id, 191.00m, "estorno-1325123456", It.IsAny<CancellationToken>()), Times.Once);
        _desfecho!.Pagamento.Status.Should().Be(StatusPagamento.Estornado);
        _desfecho.Pagamento.EstornoId.Should().Be("est-1");
        _desfecho.OrcamentoAlterado.Should().BeNull();
        _desfecho.Webhook.EventoTipo.Should().Be("Nenhum");
        _pub.VerifyNoOtherCalls();
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "EstornoAutomatico")), Times.Once);
    }

    [Fact]
    public async Task Aprovado_ValorDivergente_EstornaCancelaEPublicaRecusado()
    {
        NoGateway(StatusPagamentoGateway.Aprovado, valor: 100.00m);
        ComOrcamento(StatusOrcamento.Aprovado);
        EstornoNoGateway();

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Processado);
        _gw.Verify(g => g.EstornarAsync(Id, 100.00m, "estorno-1325123456", It.IsAny<CancellationToken>()), Times.Once);
        _desfecho!.Pagamento.Status.Should().Be(StatusPagamento.Estornado);
        _desfecho.OrcamentoAlterado!.Status.Should().Be(StatusOrcamento.Cancelado);
        _desfecho.OrcamentoAlterado.Motivo.Should().Be("valor_divergente");
        _desfecho.Webhook.EventoTipo.Should().Be("PagamentoRecusado");
        _pub.Verify(p => p.PublicarAsync(It.Is<PagamentoRecusado>(m => m.PagamentoId == Id && m.Motivo == "valor_divergente"),
            It.IsAny<CancellationToken>()), Times.Once);
        _pub.Verify(p => p.PublicarAsync(It.IsAny<PagamentoConfirmado>(), It.IsAny<CancellationToken>()), Times.Never);
        _tel.Verify(t => t.Registrar(It.Is<EventoPagamento>(e => e.Etapa == "EstornoAutomatico")), Times.Once);
    }

    [Fact]
    public async Task Pendente_SalvaPagamentoPendenteSemPublicar()
    {
        NoGateway(StatusPagamentoGateway.Pendente);
        ComOrcamento(StatusOrcamento.Aprovado);

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Pendente);
        _repo.Verify(x => x.SalvarPagamentoAsync(It.Is<Pagamento>(p => p.Id == Id && p.Status == StatusPagamento.Pendente &&
            p.Valor == 191.00m && p.OrdemServicoId == Cenario.OsId), true, It.IsAny<CancellationToken>()), Times.Once);
        NuncaRegistrou();
        _pub.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Pendente_JaSalvo_NaoSalvaDeNovo()
    {
        NoGateway(StatusPagamentoGateway.Pendente);
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamentoSalvo(StatusPagamento.Pendente);

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Pendente);
        _repo.Verify(x => x.SalvarPagamentoAsync(It.IsAny<Pagamento>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Pendente_JaRegistrado_AprovadoDepois_UsaPagamentoNovoFalse()
    {
        NoGateway(StatusPagamentoGateway.Aprovado);
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamentoSalvo(StatusPagamento.Pendente);

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Processado);
        _desfecho!.PagamentoNovo.Should().BeFalse();
        _desfecho.Pagamento.Status.Should().Be(StatusPagamento.Aprovado);
        _pub.Verify(p => p.PublicarAsync(It.IsAny<PagamentoConfirmado>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Duplicado_JaPublicado_NaoPublicaNemGrava()
    {
        NoGateway(StatusPagamentoGateway.Aprovado);
        ComOrcamento(StatusOrcamento.Aprovado);
        _repo.Setup(r => r.ObterRegistroWebhookAsync(Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistroWebhook(Id, Cenario.OsId, "PagamentoConfirmado", null, true, RelogioFixo.Agora));

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Duplicado);
        NuncaRegistrou();
        _repo.Verify(x => x.MarcarEventoPublicadoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        NuncaEstornou();
        _pub.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Duplicado_NaoPublicado_RepublicaEMarca()
    {
        NoGateway(StatusPagamentoGateway.Aprovado);
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamentoSalvo(StatusPagamento.Aprovado);
        _repo.Setup(r => r.ObterRegistroWebhookAsync(Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistroWebhook(Id, Cenario.OsId, "PagamentoConfirmado", null, false, RelogioFixo.Agora));

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Duplicado);
        _pub.Verify(p => p.PublicarAsync(It.Is<PagamentoConfirmado>(m => m.PagamentoId == Id && m.Valor == 191.00m &&
            m.Meio == "pix"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(x => x.MarcarEventoPublicadoAsync(Id, It.IsAny<CancellationToken>()), Times.Once);
        NuncaRegistrou();
        NuncaEstornou();
    }

    [Fact]
    public async Task Duplicado_RecusadoNaoPublicado_RepublicaComMotivoGravado()
    {
        NoGateway(StatusPagamentoGateway.Aprovado);
        ComOrcamento(StatusOrcamento.Cancelado);
        _repo.Setup(r => r.ObterRegistroWebhookAsync(Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistroWebhook(Id, Cenario.OsId, "PagamentoRecusado", "valor_divergente", false, RelogioFixo.Agora));

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Duplicado);
        _pub.Verify(p => p.PublicarAsync(It.Is<PagamentoRecusado>(m => m.PagamentoId == Id && m.Motivo == "valor_divergente"),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(x => x.MarcarEventoPublicadoAsync(Id, It.IsAny<CancellationToken>()), Times.Once);
        NuncaEstornou();
    }

    [Fact]
    public async Task CorridaEntreReplicas_RegistrarRetornaFalse_TrataComoDuplicado()
    {
        NoGateway(StatusPagamentoGateway.Aprovado);
        ComOrcamento(StatusOrcamento.Aprovado);
        _repo.SetupSequence(r => r.ObterRegistroWebhookAsync(Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistroWebhook?)null)
            .ReturnsAsync(new RegistroWebhook(Id, Cenario.OsId, "PagamentoConfirmado", null, true, RelogioFixo.Agora));
        _repo.Setup(r => r.RegistrarDesfechoAsync(It.IsAny<DesfechoPagamento>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Duplicado);
        _pub.VerifyNoOtherCalls();
        _repo.Verify(x => x.MarcarEventoPublicadoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PagamentoJaDecididoSemRegistroWebhook_Ignora()
    {
        NoGateway(StatusPagamentoGateway.Aprovado);
        ComOrcamento(StatusOrcamento.Aprovado);
        ComPagamentoSalvo(StatusPagamento.Aprovado);

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Ignorado);
        NuncaRegistrou();
        NuncaEstornou();
        _pub.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SemExternalReference_Ignora()
    {
        NoGateway(StatusPagamentoGateway.Aprovado, semOs: true);

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Ignorado);
        NuncaRegistrou();
        _pub.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PagamentoInexistenteNoGateway_Ignora()
    {
        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Ignorado);
        NuncaRegistrou();
        _pub.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OrcamentoInexistente_Ignora()
    {
        NoGateway(StatusPagamentoGateway.Aprovado);

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Ignorado);
        NuncaEstornou();
        NuncaRegistrou();
        _pub.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StatusEstornado_Ignora()
    {
        NoGateway(StatusPagamentoGateway.Estornado);
        ComOrcamento(StatusOrcamento.Aprovado);

        var r = await Criar().ExecutarAsync(Id, default);

        r.Should().Be(ResultadoNotificacao.Ignorado);
        NuncaRegistrou();
        _repo.Verify(x => x.SalvarPagamentoAsync(It.IsAny<Pagamento>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _pub.VerifyNoOtherCalls();
    }
}
