using System.Net;
using System.Text.Json;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Api.Testes;

public class PagamentosEndpointTestes
{
    private static FabricaApi FabricaComPagamento()
    {
        var fabrica = new FabricaApi();
        fabrica.Repositorio.Semear(Cenario.Orcamento(StatusOrcamento.Aprovado));
        var pagamento = Pagamento.Registrar("sim-77", Cenario.OsId, 191.00m, "simulado", StatusPagamento.Aprovado,
            null, new DateTime(2026, 11, 16, 13, 0, 0, DateTimeKind.Utc));
        fabrica.Repositorio.Pagamentos[pagamento.Id] = pagamento;
        return fabrica;
    }

    [Fact]
    public async Task Get_ClienteDono_ListaPagamentos()
    {
        await using var fabrica = FabricaComPagamento();
        var resposta = await fabrica.Cliente("Cliente", Cenario.CpfFormatado).GetAsync(Cenario.RotaPagamentos);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        json.ValueKind.Should().Be(JsonValueKind.Array);
        json.GetArrayLength().Should().Be(1);
        json[0].GetProperty("pagamentoId").GetString().Should().Be("sim-77");
        json[0].GetProperty("status").GetString().Should().Be("Aprovado");
        json[0].GetProperty("valor").GetDecimal().Should().Be(191.00m);
    }

    [Fact]
    public async Task Get_ClienteOutroDocumento_404()
    {
        await using var fabrica = FabricaComPagamento();
        (await fabrica.Cliente("Cliente", Cenario.OutroCpf).GetAsync(Cenario.RotaPagamentos)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_SemToken_401()
    {
        await using var fabrica = FabricaComPagamento();
        (await fabrica.CreateClient().GetAsync(Cenario.RotaPagamentos)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
