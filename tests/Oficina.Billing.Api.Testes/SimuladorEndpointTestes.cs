using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit.Testing;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Api.Testes;

public class SimuladorEndpointTestes
{
    private static FabricaApi FabricaComOrcamento(StatusOrcamento status, string modo = "Simulado",
        IDictionary<string, string?>? configuracoes = null)
    {
        var fabrica = new FabricaApi(modo, configuracoes);
        fabrica.Repositorio.Semear(Cenario.Orcamento(status));
        return fabrica;
    }

    [Fact]
    public async Task ModoSimulado_Admin_Aprovado_200()
    {
        await using var fabrica = FabricaComOrcamento(StatusOrcamento.Aprovado);
        var resposta = await fabrica.Cliente("Admin").PostAsJsonAsync(Cenario.RotaSimulador, new { resultado = "aprovado" });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        var pagamentoId = json.GetProperty("pagamentoId").GetString();
        pagamentoId.Should().StartWith("sim-");
        json.GetProperty("resultado").GetString().Should().Be("Processado");
        (await fabrica.Harness.Published.Any<PagamentoConfirmado>(x =>
            x.Context.Message.PagamentoId == pagamentoId && x.Context.Message.Valor == 191.00m)).Should().BeTrue();
    }

    [Fact]
    public async Task ModoSimulado_Admin_Recusado_200EPublicaPagamentoRecusado()
    {
        await using var fabrica = FabricaComOrcamento(StatusOrcamento.Aprovado);
        var resposta = await fabrica.Cliente("Admin").PostAsJsonAsync(Cenario.RotaSimulador, new { resultado = "recusado" });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fabrica.Harness.Published.Any<PagamentoRecusado>(x => x.Context.Message.OrdemServicoId == Cenario.OsId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ModoSimulado_Atendente_403()
    {
        await using var fabrica = FabricaComOrcamento(StatusOrcamento.Aprovado);
        (await fabrica.Cliente("Atendente").PostAsJsonAsync(Cenario.RotaSimulador, new { resultado = "aprovado" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ModoSimulado_SemToken_401()
    {
        await using var fabrica = FabricaComOrcamento(StatusOrcamento.Aprovado);
        (await fabrica.CreateClient().PostAsJsonAsync(Cenario.RotaSimulador, new { resultado = "aprovado" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ModoSimulado_OrcamentoGerado_409()
    {
        await using var fabrica = FabricaComOrcamento(StatusOrcamento.Gerado);
        (await fabrica.Cliente("Admin").PostAsJsonAsync(Cenario.RotaSimulador, new { resultado = "aprovado" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ModoSimulado_OrcamentoInexistente_404()
    {
        await using var fabrica = new FabricaApi();
        (await fabrica.Cliente("Admin").PostAsJsonAsync(Cenario.RotaSimulador, new { resultado = "aprovado" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("{\"resultado\":\"talvez\"}")]
    [InlineData("{}")]
    public async Task ModoSimulado_ResultadoInvalido_400(string corpo)
    {
        await using var fabrica = FabricaComOrcamento(StatusOrcamento.Aprovado);
        var resposta = await fabrica.Cliente("Admin").PostAsync(Cenario.RotaSimulador,
            new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ModoMercadoPago_Rota404()
    {
        await using var fabrica = FabricaComOrcamento(StatusOrcamento.Aprovado, "MercadoPago",
            new Dictionary<string, string?> { ["MercadoPago:AccessToken"] = "TEST-token" });
        (await fabrica.Cliente("Admin").PostAsJsonAsync(Cenario.RotaSimulador, new { resultado = "aprovado" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
