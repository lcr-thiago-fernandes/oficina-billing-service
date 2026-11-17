using System.Net;
using System.Text.Json;

namespace Oficina.Billing.Api.Testes;

public class PathBaseESwaggerTestes
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/billing/health")]
    public async Task Health_200(string rota)
    {
        await using var fabrica = new FabricaApi();
        var resposta = await fabrica.CreateClient().GetAsync(rota);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("\"status\":\"ok\"");
    }

    [Fact]
    public async Task Swagger_SobPathBase_200()
    {
        await using var fabrica = new FabricaApi();
        var resposta = await fabrica.CreateClient().GetAsync("/billing/swagger/v1/swagger.json");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await resposta.Content.ReadAsStringAsync();
        corpo.Should().Contain("/api/v1/orcamentos/{osId}");
        corpo.Should().Contain("/webhooks/mercadopago");
        using var documento = JsonDocument.Parse(corpo);
        documento.RootElement.GetProperty("servers")[0].GetProperty("url").GetString().Should().Be("/billing");
    }

    [Fact]
    public async Task SwaggerUi_SobPathBase_200()
    {
        await using var fabrica = new FabricaApi();
        (await fabrica.CreateClient().GetAsync("/billing/swagger/index.html")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resposta_EcoaXCorrelationId()
    {
        await using var fabrica = new FabricaApi();
        var requisicao = new HttpRequestMessage(HttpMethod.Get, "/billing/health");
        requisicao.Headers.Add("X-Correlation-Id", "abc-123");

        var resposta = await fabrica.CreateClient().SendAsync(requisicao);

        resposta.Headers.GetValues("X-Correlation-Id").Should().ContainSingle().Which.Should().Be("abc-123");
    }
}
