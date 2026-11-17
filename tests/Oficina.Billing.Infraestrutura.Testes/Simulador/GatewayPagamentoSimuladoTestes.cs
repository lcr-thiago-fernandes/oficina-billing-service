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

        id.Should().MatchRegex("^sim-a-[0-9a-f]{32}-191.00-[0-9a-f]{32}$");
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

    [Theory]
    [InlineData("sim-999")]
    [InlineData("sim-x-00000000000000000000000000000000-1-00000000000000000000000000000000")]
    [InlineData("sim-a-nao-guid-1-00000000000000000000000000000000")]
    [InlineData("123456")]
    public async Task ObterDesconhecido_Null(string id) =>
        (await Criar().ObterPagamentoAsync(id, default)).Should().BeNull();

    [Fact]
    public async Task EstornarIdInvalido_Lanca()
    {
        var acao = () => Criar().EstornarAsync("sim-999", 1m, "estorno-sim-999", default);

        await acao.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OutraInstancia_ConsultaEEstornaPagamentoEmitidoPelaPrimeira()
    {
        // Duas replicas (ou a mesma apos um redeploy): nenhum estado compartilhado em memoria.
        var podA = Criar();
        var podB = Criar();
        var osId = Guid.NewGuid();
        var aprovado = podA.RegistrarPagamento(osId, 191.00m, aprovado: true);
        var recusado = podA.RegistrarPagamento(osId, 191.00m, aprovado: false);

        (await podB.ObterPagamentoAsync(aprovado, default)).Should()
            .Be(new PagamentoGateway(aprovado, osId, StatusPagamentoGateway.Aprovado, null, 191.00m, "simulado"));
        (await podB.ObterPagamentoAsync(recusado, default)).Should()
            .Be(new PagamentoGateway(recusado, osId, StatusPagamentoGateway.Recusado, "cc_rejected_simulado", 191.00m,
                "simulado"));

        var estorno = await podB.EstornarAsync(aprovado, 191.00m, $"estorno-{aprovado}", default);

        estorno.Should().Be(new EstornoGateway($"sim-estorno-{aprovado}", 191.00m));
        (await podB.ObterPagamentoAsync(aprovado, default))!.Status.Should().Be(StatusPagamentoGateway.Estornado);
    }

    [Fact]
    public void DuasInstancias_PagamentosConsecutivosNuncaColidem()
    {
        // Antes: sim-1, sim-2... reiniciavam por pod/redeploy e colidiam com WEBHOOK# (90 dias) => OS presa.
        var podA = Criar();
        var podB = Criar();
        var osId = Guid.NewGuid();

        var ids = new[]
        {
            podA.RegistrarPagamento(osId, 10m, aprovado: true),
            podB.RegistrarPagamento(osId, 10m, aprovado: true),
            Criar().RegistrarPagamento(osId, 10m, aprovado: true),
            Criar().RegistrarPagamento(Guid.NewGuid(), 10m, aprovado: true),
        };

        ids.Should().OnlyHaveUniqueItems();
    }
}
