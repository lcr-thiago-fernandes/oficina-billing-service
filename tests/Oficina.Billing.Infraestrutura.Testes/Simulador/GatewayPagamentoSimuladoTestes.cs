using Microsoft.Extensions.Options;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Infraestrutura.Simulador;

namespace Oficina.Billing.Infraestrutura.Testes.Simulador;

public class GatewayPagamentoSimuladoTestes
{
    private static GatewayPagamentoSimulado Criar(bool simularFalhaEstorno = false) =>
        new(Options.Create(new OpcoesPagamento { SimularFalhaEstorno = simularFalhaEstorno }));

    [Fact]
    public void Nome_Simulado() => Criar().Nome.Should().Be("Simulado");

    [Fact]
    public void Opcoes_PadraoSimulado()
    {
        OpcoesPagamento.Secao.Should().Be("Pagamento");
        new OpcoesPagamento().Modo.Should().Be(ModoPagamento.Simulado);
    }

    [Fact]
    public async Task CriarCobranca_RetornaPreferenciaELinkDaOs()
    {
        var osId = Guid.NewGuid();

        var cobranca = await Criar().CriarCobrancaAsync(
            new NovaCobranca(osId, 7, null, [new ItemCobranca("Troca de oleo", 1, 91.00m)]), default);

        cobranca.Should().Be(new CobrancaCriada($"sim-pref-{osId}", $"https://simulador.local/pagamentos/{osId}"));
    }

    [Fact]
    public async Task RegistrarAprovado_ObterRetornaAprovadoComOsId()
    {
        var gateway = Criar();
        var osId = Guid.NewGuid();

        var id = gateway.RegistrarPagamento(osId, 191.00m, aprovado: true);

        id.Should().MatchRegex("^sim-[0-9]+$");
        var pagamento = await gateway.ObterPagamentoAsync(id, default);
        pagamento.Should().Be(new PagamentoGateway(id, osId, StatusPagamentoGateway.Aprovado, null, 191.00m, "simulado"));
    }

    [Fact]
    public async Task RegistrarRecusado_DetalheSimulado()
    {
        var gateway = Criar();

        var id = gateway.RegistrarPagamento(Guid.NewGuid(), 50m, aprovado: false);

        var pagamento = await gateway.ObterPagamentoAsync(id, default);
        pagamento!.Status.Should().Be(StatusPagamentoGateway.Recusado);
        pagamento.DetalheStatus.Should().Be("cc_rejected_simulado");
        pagamento.Meio.Should().Be("simulado");
    }

    [Fact]
    public void IdsSaoUnicosEmParalelo()
    {
        var gateway = Criar();
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 500, _ => ids.Add(gateway.RegistrarPagamento(Guid.NewGuid(), 10m, aprovado: true)));

        ids.Should().HaveCount(500).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Estornar_RetornaIdDeEstornoEMarcaEstornado()
    {
        var gateway = Criar();
        var id = gateway.RegistrarPagamento(Guid.NewGuid(), 191.00m, aprovado: true);

        var estorno = await gateway.EstornarAsync(id, 191.00m, $"estorno-{id}", default);

        estorno.Should().Be(new EstornoGateway($"sim-estorno-{id}", 191.00m));
        (await gateway.ObterPagamentoAsync(id, default))!.Status.Should().Be(StatusPagamentoGateway.Estornado);
    }

    [Fact]
    public async Task Estornar_ComFalhaSimulada_Lanca()
    {
        var gateway = Criar(simularFalhaEstorno: true);
        var id = gateway.RegistrarPagamento(Guid.NewGuid(), 191.00m, aprovado: true);

        var acao = () => gateway.EstornarAsync(id, 191.00m, $"estorno-{id}", default);

        await acao.Should().ThrowAsync<GatewayPagamentoIndisponivelException>();
        (await gateway.ObterPagamentoAsync(id, default))!.Status.Should().Be(StatusPagamentoGateway.Aprovado);
    }

    [Fact]
    public async Task ObterDesconhecido_Null() =>
        (await Criar().ObterPagamentoAsync("sim-999", default)).Should().BeNull();
}
