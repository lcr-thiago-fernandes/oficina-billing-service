using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Oficina.Billing.Api.Testes;

public class HealthTestes : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _fabrica;

    public HealthTestes(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica;
    }

    [Theory]
    [InlineData("/health")]
    public async Task Health_RespondeOk(string rota)
    {
        var resposta = await _fabrica.CreateClient().GetAsync(rota);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("\"status\":\"ok\"");
    }
}
