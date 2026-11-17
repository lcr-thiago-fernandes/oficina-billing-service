using System.Net;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Api.Testes;

public class WebhookEndpointTestes
{
    private const string RequestId = "bb56a2f1-6aae-46ac-982e-9dcd3581d08e";

    private static string Rota(string dataId, string tipo = "payment") =>
        $"/billing/webhooks/mercadopago?data.id={dataId}&type={tipo}";

    private static HttpRequestMessage Requisicao(string rota, string? assinatura, string? requestId = RequestId)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, rota)
        {
            Content = new StringContent("{\"action\":\"payment.updated\"}", System.Text.Encoding.UTF8, "application/json"),
        };
        if (assinatura is not null) requisicao.Headers.TryAddWithoutValidation("x-signature", assinatura);
        if (requestId is not null) requisicao.Headers.TryAddWithoutValidation("x-request-id", requestId);
        return requisicao;
    }

    private static HttpRequestMessage RequisicaoAssinada(string dataId, string tipo = "payment") =>
        Requisicao(Rota(dataId, tipo), FabricaApi.AssinarWebhook(dataId, RequestId));

    private static FabricaApi FabricaComOrcamentoAprovado(Action<IServiceCollection>? servicos = null)
    {
        var fabrica = new FabricaApi(servicos: servicos);
        fabrica.Repositorio.Semear(Cenario.Orcamento(StatusOrcamento.Aprovado));
        return fabrica;
    }

    [Fact]
    public async Task SemAssinatura_401()
    {
        await using var fabrica = FabricaComOrcamentoAprovado();
        (await fabrica.CreateClient().SendAsync(Requisicao(Rota("123"), assinatura: null))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AssinaturaInvalida_401()
    {
        await using var fabrica = FabricaComOrcamentoAprovado();
        var assinatura = FabricaApi.AssinarWebhook("123", RequestId, segredo: "outro-segredo");
        (await fabrica.CreateClient().SendAsync(Requisicao(Rota("123"), assinatura))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("lixo")]
    [InlineData("ts=1742505638683")]
    [InlineData("ts=1742505638683,v1=nao-e-hex")]
    [InlineData(",,=,")]
    public async Task HeaderMalformado_401NaoQuinhentos(string assinatura)
    {
        await using var fabrica = FabricaComOrcamentoAprovado();
        (await fabrica.CreateClient().SendAsync(Requisicao(Rota("123"), assinatura))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TipoNaoPayment_200SemProcessar()
    {
        await using var fabrica = FabricaComOrcamentoAprovado();
        var cliente = fabrica.CreateClient();
        var pagamentoId = fabrica.GatewaySimulado.RegistrarPagamento(Cenario.OsId, 191.00m, aprovado: true);

        var resposta = await cliente.SendAsync(RequisicaoAssinada(pagamentoId, tipo: "merchant_order"));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        fabrica.Repositorio.Pagamentos.Should().BeEmpty();
        fabrica.Repositorio.Webhooks.Should().BeEmpty();
        fabrica.Harness.Published.Select<PagamentoConfirmado>().Should().BeEmpty();
    }

    [Fact]
    public async Task TopicSemType_UsaTopicComoTipo()
    {
        await using var fabrica = FabricaComOrcamentoAprovado();
        var cliente = fabrica.CreateClient();
        var pagamentoId = fabrica.GatewaySimulado.RegistrarPagamento(Cenario.OsId, 191.00m, aprovado: true);
        var rota = $"/billing/webhooks/mercadopago?data.id={pagamentoId}&topic=payment";

        var resposta = await cliente.SendAsync(Requisicao(rota, FabricaApi.AssinarWebhook(pagamentoId, RequestId)));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        fabrica.Repositorio.Webhooks.Should().ContainKey(pagamentoId);
    }

    [Fact]
    public async Task AssinaturaValida_PagamentoSimuladoAprovado_200EPublicaPagamentoConfirmado()
    {
        await using var fabrica = FabricaComOrcamentoAprovado();
        var cliente = fabrica.CreateClient();
        var pagamentoId = fabrica.GatewaySimulado.RegistrarPagamento(Cenario.OsId, 191.00m, aprovado: true);

        var resposta = await cliente.SendAsync(RequisicaoAssinada(pagamentoId));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fabrica.Harness.Published.Any<PagamentoConfirmado>(x =>
            x.Context.Message.OrdemServicoId == Cenario.OsId && x.Context.Message.PagamentoId == pagamentoId))
            .Should().BeTrue();
        fabrica.Repositorio.Pagamentos[pagamentoId].Status.Should().Be(StatusPagamento.Aprovado);
    }

    [Fact]
    public async Task GatewayIndisponivel_500()
    {
        var gateway = new Mock<IGatewayPagamento>();
        gateway.SetupGet(g => g.Nome).Returns("Simulado");
        gateway.Setup(g => g.ObterPagamentoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GatewayPagamentoIndisponivelException("MP fora do ar"));
        await using var fabrica = FabricaComOrcamentoAprovado(s => s.AddSingleton(gateway.Object));

        var resposta = await fabrica.CreateClient().SendAsync(RequisicaoAssinada("123"));

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Concorrencia_500()
    {
        await using var fabrica = FabricaComOrcamentoAprovado();
        var cliente = fabrica.CreateClient();
        fabrica.Repositorio.SimularConcorrenciaNoDesfecho = true;
        var pagamentoId = fabrica.GatewaySimulado.RegistrarPagamento(Cenario.OsId, 191.00m, aprovado: true);

        var resposta = await cliente.SendAsync(RequisicaoAssinada(pagamentoId));

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ReentregaDoMesmoWebhook_200PublicaUmaVez()
    {
        await using var fabrica = FabricaComOrcamentoAprovado();
        var cliente = fabrica.CreateClient();
        var pagamentoId = fabrica.GatewaySimulado.RegistrarPagamento(Cenario.OsId, 191.00m, aprovado: true);

        (await cliente.SendAsync(RequisicaoAssinada(pagamentoId))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await cliente.SendAsync(RequisicaoAssinada(pagamentoId))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await fabrica.Harness.Published.Any<PagamentoConfirmado>()).Should().BeTrue();
        fabrica.Harness.Published.Select<PagamentoConfirmado>().Count().Should().Be(1);
    }
}
