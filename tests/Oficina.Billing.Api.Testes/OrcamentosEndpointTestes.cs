using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit.Testing;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Api.Testes;

public class OrcamentosEndpointTestes
{
    private static FabricaApi FabricaComOrcamento(StatusOrcamento status = StatusOrcamento.Gerado)
    {
        var fabrica = new FabricaApi();
        fabrica.Repositorio.Semear(Cenario.Orcamento(status));
        return fabrica;
    }

    private static async Task<JsonElement> LerJsonAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Get_SemToken_401()
    {
        await using var fabrica = FabricaComOrcamento();
        (await fabrica.CreateClient().GetAsync(Cenario.RotaOrcamento)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_ClienteDono_200ComValorTotal()
    {
        await using var fabrica = FabricaComOrcamento();
        var resposta = await fabrica.Cliente("Cliente", Cenario.Cpf).GetAsync(Cenario.RotaOrcamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await LerJsonAsync(resposta);
        json.GetProperty("valorTotal").GetDecimal().Should().Be(191.00m);
        json.GetProperty("status").GetString().Should().Be("Gerado");
        json.GetProperty("ordemServicoId").GetGuid().Should().Be(Cenario.OsId);
    }

    [Fact]
    public async Task Get_ClienteDonoComTokenCpfFormatado_200()
    {
        await using var fabrica = FabricaComOrcamento();
        (await fabrica.Cliente("Cliente", Cenario.CpfFormatado).GetAsync(Cenario.RotaOrcamento)).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_ClienteOutroDocumento_404()
    {
        await using var fabrica = FabricaComOrcamento();
        var resposta = await fabrica.Cliente("Cliente", Cenario.OutroCpf).GetAsync(Cenario.RotaOrcamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Get_OsInexistente_404()
    {
        await using var fabrica = new FabricaApi();
        (await fabrica.Cliente("Admin").GetAsync(Cenario.RotaOrcamento)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_ClienteSemClaimDocumento_403()
    {
        await using var fabrica = FabricaComOrcamento();
        (await fabrica.Cliente("Cliente").GetAsync(Cenario.RotaOrcamento)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_PerfilDesconhecido_403()
    {
        await using var fabrica = FabricaComOrcamento();
        (await fabrica.Cliente("Mecanico").GetAsync(Cenario.RotaOrcamento)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_Atendente_200()
    {
        await using var fabrica = FabricaComOrcamento();
        (await fabrica.Cliente("Atendente").GetAsync(Cenario.RotaOrcamento)).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_Admin_200()
    {
        await using var fabrica = FabricaComOrcamento();
        (await fabrica.Cliente("Admin").GetAsync(Cenario.RotaOrcamento)).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Aprovacao_ClienteDono_200ComLinkEPublicaOrcamentoAprovado()
    {
        await using var fabrica = FabricaComOrcamento();
        var resposta = await fabrica.Cliente("Cliente", Cenario.CpfFormatado)
            .PostAsync($"{Cenario.RotaOrcamento}/aprovacao", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await LerJsonAsync(resposta);
        json.GetProperty("status").GetString().Should().Be("Aprovado");
        json.GetProperty("linkPagamento").GetString().Should().Be($"https://simulador.local/pagamentos/{Cenario.OsId}");
        (await fabrica.Harness.Published.Any<OrcamentoAprovado>(x =>
            x.Context.Message.OrdemServicoId == Cenario.OsId)).Should().BeTrue();
    }

    [Fact]
    public async Task Aprovacao_Repetida_200MesmoLink()
    {
        await using var fabrica = FabricaComOrcamento();
        var cliente = fabrica.Cliente("Cliente", Cenario.Cpf);

        var primeira = await LerJsonAsync(await cliente.PostAsync($"{Cenario.RotaOrcamento}/aprovacao", null));
        var segunda = await cliente.PostAsync($"{Cenario.RotaOrcamento}/aprovacao", null);

        segunda.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LerJsonAsync(segunda)).GetProperty("linkPagamento").GetString()
            .Should().Be(primeira.GetProperty("linkPagamento").GetString());
    }

    [Fact]
    public async Task Aprovacao_ClienteOutroDocumento_404()
    {
        await using var fabrica = FabricaComOrcamento();
        (await fabrica.Cliente("Cliente", Cenario.OutroCpf).PostAsync($"{Cenario.RotaOrcamento}/aprovacao", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rejeicao_Atendente_200EPublicaOrcamentoRejeitado()
    {
        await using var fabrica = FabricaComOrcamento();
        var resposta = await fabrica.Cliente("Atendente")
            .PostAsJsonAsync($"{Cenario.RotaOrcamento}/rejeicao", new { motivo = "caro demais" });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await LerJsonAsync(resposta);
        json.GetProperty("status").GetString().Should().Be("Rejeitado");
        json.GetProperty("motivo").GetString().Should().Be("caro demais");
        (await fabrica.Harness.Published.Any<OrcamentoRejeitado>(x =>
            x.Context.Message.OrdemServicoId == Cenario.OsId && x.Context.Message.Motivo == "caro demais"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Rejeicao_SemCorpo_200()
    {
        await using var fabrica = FabricaComOrcamento();
        (await fabrica.Cliente("Cliente", Cenario.Cpf).PostAsync($"{Cenario.RotaOrcamento}/rejeicao", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rejeicao_AposAprovacao_409()
    {
        await using var fabrica = FabricaComOrcamento(StatusOrcamento.Aprovado);
        var resposta = await fabrica.Cliente("Atendente")
            .PostAsJsonAsync($"{Cenario.RotaOrcamento}/rejeicao", new { motivo = "desisti" });

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task TokenComAudienceErrada_401()
    {
        await using var fabrica = FabricaComOrcamento();
        var token = GeradorTokenTeste.Gerar("Admin", audiencia: "outra-api");
        (await fabrica.ClienteComToken(token).GetAsync(Cenario.RotaOrcamento)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenComIssuerErrado_401()
    {
        await using var fabrica = FabricaComOrcamento();
        var token = GeradorTokenTeste.Gerar("Admin", emissor: "outro-emissor");
        (await fabrica.ClienteComToken(token).GetAsync(Cenario.RotaOrcamento)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenComAssinaturaErrada_401()
    {
        await using var fabrica = FabricaComOrcamento();
        var token = GeradorTokenTeste.Gerar("Admin", segredo: "outro-segredo-com-pelo-menos-32-caracteres!!");
        (await fabrica.ClienteComToken(token).GetAsync(Cenario.RotaOrcamento)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenExpirado_401()
    {
        await using var fabrica = FabricaComOrcamento();
        var token = GeradorTokenTeste.Gerar("Admin", expirado: true);
        (await fabrica.ClienteComToken(token).GetAsync(Cenario.RotaOrcamento)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
