using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Infraestrutura.MercadoPago;

namespace Oficina.Billing.Infraestrutura.Testes.MercadoPago;

public class MercadoPagoGatewayTestes
{
    private static readonly Guid OsId = Guid.Parse("3f2b8c1e-5d4a-4b7e-9a10-1c2d3e4f5a6b");

    private static MercadoPagoGateway Criar(HandlerHttpFalso handler) =>
        new(new HttpClient(handler),
            Options.Create(new OpcoesMercadoPago
            {
                UrlBase = "https://api.mp.teste",
                AccessToken = "TOKEN-SECRETO",
                UrlNotificacao = "https://exemplo.com/billing/webhooks/mercadopago",
            }));

    [Fact]
    public async Task CriarCobranca_EnviaPreferenciaComExternalReferenceEBearer()
    {
        var handler = HandlerHttpFalso.Json(HttpStatusCode.Created,
            """{"id":"pref-1","init_point":"https://mp.com/checkout?pref_id=pref-1"}""");
        var cobranca = new NovaCobranca(OsId, 7, "cli@exemplo.com",
            [new ItemCobranca("Troca de oleo", 1, 120m), new ItemCobranca("Filtro", 2, 35.50m)]);

        var r = await Criar(handler).CriarCobrancaAsync(cobranca, CancellationToken.None);

        r.PreferenciaId.Should().Be("pref-1");
        r.LinkPagamento.Should().Be("https://mp.com/checkout?pref_id=pref-1");
        var req = handler.Requisicoes.Single();
        req.Metodo.Should().Be(HttpMethod.Post);
        req.Uri.AbsolutePath.Should().Be("/checkout/preferences");
        req.Cabecalhos.Authorization!.ToString().Should().Be("Bearer TOKEN-SECRETO");
        req.Cabecalhos.GetValues("X-Idempotency-Key").Should().ContainSingle($"preferencia-{OsId}");
        using var json = JsonDocument.Parse(req.Corpo!);
        var raiz = json.RootElement;
        raiz.GetProperty("external_reference").GetString().Should().Be(OsId.ToString());
        raiz.GetProperty("notification_url").GetString().Should().Be("https://exemplo.com/billing/webhooks/mercadopago");
        raiz.GetProperty("payer").GetProperty("email").GetString().Should().Be("cli@exemplo.com");
        var item = raiz.GetProperty("items")[1];
        item.GetProperty("unit_price").GetDecimal().Should().Be(35.50m);
        item.GetProperty("quantity").GetInt32().Should().Be(2);
        item.GetProperty("currency_id").GetString().Should().Be("BRL");
    }

    [Fact]
    public async Task CriarCobranca_SemEmail_OmitePayer()
    {
        var handler = HandlerHttpFalso.Json(HttpStatusCode.Created, """{"id":"p","init_point":"u"}""");

        await Criar(handler).CriarCobrancaAsync(new NovaCobranca(OsId, 1, null, [new ItemCobranca("x", 1, 1m)]),
            CancellationToken.None);

        using var json = JsonDocument.Parse(handler.Requisicoes.Single().Corpo!);
        json.RootElement.TryGetProperty("payer", out _).Should().BeFalse();
    }

    private static string PagamentoJson(string status, string externalReference) =>
        "{\"id\":1325123456,\"status\":\"" + status + "\",\"status_detail\":\"accredited\"," +
        "\"external_reference\":\"" + externalReference + "\",\"transaction_amount\":191.00,\"payment_method_id\":\"pix\"}";

    [Fact]
    public async Task ObterPagamento_Approved_MapeiaParaAprovado()
    {
        var handler = HandlerHttpFalso.Json(HttpStatusCode.OK, PagamentoJson("approved", OsId.ToString()));

        var p = await Criar(handler).ObterPagamentoAsync("1325123456", CancellationToken.None);

        p.Should().NotBeNull();
        p!.Status.Should().Be(StatusPagamentoGateway.Aprovado);
        p.Valor.Should().Be(191.00m);
        p.Meio.Should().Be("pix");
        p.OrdemServicoId.Should().Be(OsId);
        p.PagamentoId.Should().Be("1325123456");
        p.DetalheStatus.Should().Be("accredited");
        var req = handler.Requisicoes.Single();
        req.Metodo.Should().Be(HttpMethod.Get);
        req.Uri.AbsolutePath.Should().Be("/v1/payments/1325123456");
        req.Cabecalhos.Authorization!.ToString().Should().Be("Bearer TOKEN-SECRETO");
    }

    [Theory]
    [InlineData("approved", StatusPagamentoGateway.Aprovado)]
    [InlineData("rejected", StatusPagamentoGateway.Recusado)]
    [InlineData("cancelled", StatusPagamentoGateway.Recusado)]
    [InlineData("pending", StatusPagamentoGateway.Pendente)]
    [InlineData("in_process", StatusPagamentoGateway.Pendente)]
    [InlineData("authorized", StatusPagamentoGateway.Pendente)]
    [InlineData("in_mediation", StatusPagamentoGateway.Pendente)]
    [InlineData("refunded", StatusPagamentoGateway.Estornado)]
    [InlineData("charged_back", StatusPagamentoGateway.Estornado)]
    public async Task ObterPagamento_MapeiaStatus(string mp, StatusPagamentoGateway esperado)
    {
        var handler = HandlerHttpFalso.Json(HttpStatusCode.OK, PagamentoJson(mp, OsId.ToString()));

        var p = await Criar(handler).ObterPagamentoAsync("1", CancellationToken.None);

        p!.Status.Should().Be(esperado);
    }

    [Fact]
    public async Task ObterPagamento_StatusDesconhecido_TrataComoPendente()
    {
        var handler = HandlerHttpFalso.Json(HttpStatusCode.OK, PagamentoJson("novo_status", OsId.ToString()));

        (await Criar(handler).ObterPagamentoAsync("1", CancellationToken.None))!.Status
            .Should().Be(StatusPagamentoGateway.Pendente);
    }

    [Fact]
    public async Task ObterPagamento_404_RetornaNull()
    {
        var handler = HandlerHttpFalso.Json(HttpStatusCode.NotFound, """{"message":"Payment not found"}""");

        (await Criar(handler).ObterPagamentoAsync("999", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ObterPagamento_ExternalReferenceInvalida_OrdemServicoIdNull()
    {
        var handler = HandlerHttpFalso.Json(HttpStatusCode.OK, PagamentoJson("approved", "nao-e-guid"));

        (await Criar(handler).ObterPagamentoAsync("1", CancellationToken.None))!.OrdemServicoId.Should().BeNull();
    }

    [Fact]
    public async Task ObterPagamento_4xx_LancaInvalidOperationSemToken()
    {
        var handler = HandlerHttpFalso.Json(HttpStatusCode.Unauthorized, """{"message":"invalid token"}""");

        var act = () => Criar(handler).ObterPagamentoAsync("1", CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain("invalid token").And.NotContain("TOKEN-SECRETO");
    }

    [Fact]
    public async Task Estornar_EnviaChaveIdempotencia()
    {
        var handler = HandlerHttpFalso.Json(HttpStatusCode.Created, """{"id":98765,"amount":191.00}""");

        var r = await Criar(handler).EstornarAsync("1325123456", 191m, "estorno-1325123456", CancellationToken.None);

        r.EstornoId.Should().Be("98765");
        r.Valor.Should().Be(191.00m);
        var req = handler.Requisicoes.Single();
        req.Metodo.Should().Be(HttpMethod.Post);
        req.Uri.AbsolutePath.Should().Be("/v1/payments/1325123456/refunds");
        req.Cabecalhos.GetValues("X-Idempotency-Key").Should().ContainSingle("estorno-1325123456");
        req.Cabecalhos.Authorization!.ToString().Should().Be("Bearer TOKEN-SECRETO");
        req.Corpo.Should().Be("{}");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Estornar_5xx_LancaIndisponivel(int status)
    {
        var handler = HandlerHttpFalso.Json((HttpStatusCode)status, "{}");

        var act = () => Criar(handler).EstornarAsync("1", 10m, "estorno-1", CancellationToken.None);

        await act.Should().ThrowAsync<GatewayPagamentoIndisponivelException>();
    }

    [Fact]
    public async Task Estornar_4xx_LancaInvalidOperation()
    {
        var handler = HandlerHttpFalso.Json(HttpStatusCode.BadRequest, """{"message":"refund not allowed"}""");

        var act = () => Criar(handler).EstornarAsync("1", 10m, "estorno-1", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*refund not allowed*");
    }

    [Fact]
    public async Task Estornar_Timeout_LancaIndisponivel()
    {
        var handler = new HandlerHttpFalso(_ => throw new TaskCanceledException("timeout", new TimeoutException()));

        var act = () => Criar(handler).EstornarAsync("1", 10m, "estorno-1", CancellationToken.None);

        await act.Should().ThrowAsync<GatewayPagamentoIndisponivelException>();
    }

    [Fact]
    public async Task Estornar_FalhaDeRede_LancaIndisponivel()
    {
        var handler = new HandlerHttpFalso(_ => throw new HttpRequestException("sem rede"));

        var act = () => Criar(handler).EstornarAsync("1", 10m, "estorno-1", CancellationToken.None);

        await act.Should().ThrowAsync<GatewayPagamentoIndisponivelException>();
    }

    [Fact]
    public async Task Estornar_CancelamentoDoChamador_PropagaOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        var handler = new HandlerHttpFalso(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        var act = () => Criar(handler).EstornarAsync("1", 10m, "estorno-1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
