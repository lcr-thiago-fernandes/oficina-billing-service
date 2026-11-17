using MassTransit;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Infraestrutura.Testes.Mensageria;

public class ConsumidoresTestes
{
    private static readonly DateTime Agora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static GerarOrcamento ComandoGerar(Guid osId) => new()
    {
        OrdemServicoId = osId,
        OcorridoEm = Agora,
        NumeroOs = 7,
        ClienteDocumento = "529.982.247-25",
        ClienteNome = "Maria",
        ClienteEmail = "m@x.com",
        Itens =
        [
            new ItemGerarOrcamento { Tipo = TipoItemOrcamento.Servico, ReferenciaId = Guid.NewGuid(), Descricao = "Troca de oleo", Quantidade = 1, PrecoUnitario = 91.00m },
            new ItemGerarOrcamento { Tipo = TipoItemOrcamento.Peca, ReferenciaId = Guid.NewGuid(), Descricao = "Filtro", Quantidade = 2, PrecoUnitario = 50.00m },
        ],
    };

    private static Orcamento OrcamentoNoStatus(Guid osId, StatusOrcamento status)
    {
        var o = Orcamento.Gerar(osId, 7, new DadosCliente("52998224725", "Maria", "m@x.com"),
            [new ItemOrcamento(TipoItem.Servico, Guid.NewGuid(), "Troca de oleo", 1, 191.00m)], Agora);
        return Orcamento.Reconstituir(o.Id, o.OrdemServicoId, o.NumeroOs, o.Cliente, o.Itens, o.ValorTotal, status,
            null, status == StatusOrcamento.Aprovado ? "sim-pref" : null,
            status == StatusOrcamento.Aprovado ? "https://simulador.local" : null, Agora, Agora, 0);
    }

    [Fact]
    public async Task GerarOrcamento_PublicaOrcamentoGerado()
    {
        var repo = new RepositorioEmMemoria();
        await using var h = await HarnessBilling.IniciarAsync(repo);
        var osId = Guid.NewGuid();

        await h.EnviarComandoAsync(ComandoGerar(osId));

        (await h.Harness.Published.Any<OrcamentoGerado>(p => p.Context.Message.OrdemServicoId == osId))
            .Should().BeTrue();
        var gerado = h.Harness.Published.Select<OrcamentoGerado>().Single().Context.Message;
        gerado.ValorTotal.Should().Be(191.00m);
        gerado.OrcamentoId.Should().Be(repo.Orcamentos[osId].Id);
    }

    [Fact]
    public async Task GerarOrcamento_Duplicado_PublicaDuasVezesSemDuplicarRegistro()
    {
        var repo = new RepositorioEmMemoria();
        await using var h = await HarnessBilling.IniciarAsync(repo);
        var osId = Guid.NewGuid();

        await h.EnviarComandoAsync(ComandoGerar(osId));
        (await h.Harness.Published.Any<OrcamentoGerado>()).Should().BeTrue();
        await h.EnviarComandoAsync(ComandoGerar(osId));
        var consumidos = 0;
        await foreach (var _ in h.Harness.Consumed.SelectAsync<GerarOrcamento>())
            if (++consumidos == 2)
                break;
        await h.Harness.InactivityTask;

        var publicados = h.Harness.Published.Select<OrcamentoGerado>().Select(p => p.Context.Message).ToList();
        publicados.Should().HaveCount(2);
        publicados.Select(m => m.OrcamentoId).Distinct().Should().ContainSingle()
            .Which.Should().Be(repo.Orcamentos[osId].Id);
        repo.Insercoes.Should().Be(1);
    }

    [Fact]
    public async Task CancelarOrcamento_ConsumidoSemFalha()
    {
        var repo = new RepositorioEmMemoria();
        var osId = Guid.NewGuid();
        repo.Orcamentos[osId] = OrcamentoNoStatus(osId, StatusOrcamento.Gerado);
        await using var h = await HarnessBilling.IniciarAsync(repo);

        await h.EnviarComandoAsync(new CancelarOrcamento { OrdemServicoId = osId, OcorridoEm = Agora, Motivo = "OS cancelada" });

        (await h.Harness.Consumed.Any<CancelarOrcamento>()).Should().BeTrue();
        await h.Harness.InactivityTask;
        h.Harness.Consumed.Select<CancelarOrcamento>().Should().OnlyContain(c => c.Exception == null);
        h.Harness.Published.Select<Fault<CancelarOrcamento>>().Should().BeEmpty();
        repo.Orcamentos[osId].Status.Should().Be(StatusOrcamento.Cancelado);
    }

    [Fact]
    public async Task EstornarPagamento_ComGatewayOk_PublicaPagamentoEstornado()
    {
        var repo = new RepositorioEmMemoria();
        var osId = Guid.NewGuid();
        repo.Orcamentos[osId] = OrcamentoNoStatus(osId, StatusOrcamento.Aprovado);
        await using var h = await HarnessBilling.IniciarAsync(repo);
        var pagamentoId = h.Gateway.RegistrarPagamento(osId, 191.00m, aprovado: true);
        repo.Pagamentos[pagamentoId] = Pagamento.Registrar(pagamentoId, osId, 191.00m, "simulado",
            StatusPagamento.Aprovado, null, Agora);

        await h.EnviarComandoAsync(new EstornarPagamento { OrdemServicoId = osId, OcorridoEm = Agora, Motivo = "Falha na execucao" });

        (await h.Harness.Published.Any<PagamentoEstornado>()).Should().BeTrue();
        h.Harness.Published.Select<PagamentoEstornado>().Single().Context.Message.PagamentoId.Should().Be(pagamentoId);
        repo.Pagamentos[pagamentoId].EstornoId.Should().Be($"sim-estorno-{pagamentoId}");
        h.ChamadasEstorno.Should().Be(1);
    }

    [Fact]
    public async Task EstornarPagamento_GatewayFora_PublicaEstornoFalhouAposRetries()
    {
        var repo = new RepositorioEmMemoria();
        var osId = Guid.NewGuid();
        repo.Orcamentos[osId] = OrcamentoNoStatus(osId, StatusOrcamento.Aprovado);
        await using var h = await HarnessBilling.IniciarAsync(repo, simularFalhaEstorno: true);
        var pagamentoId = h.Gateway.RegistrarPagamento(osId, 191.00m, aprovado: true);
        repo.Pagamentos[pagamentoId] = Pagamento.Registrar(pagamentoId, osId, 191.00m, "simulado",
            StatusPagamento.Aprovado, null, Agora);

        await h.EnviarComandoAsync(new EstornarPagamento { OrdemServicoId = osId, OcorridoEm = Agora, Motivo = "Falha na execucao" });

        (await h.Harness.Published.Any<EstornoFalhou>()).Should().BeTrue();
        await h.Harness.InactivityTask;
        h.Harness.Published.Select<EstornoFalhou>().Single().Context.Message.Motivo
            .Should().Be(EstornarPagamentoCasoDeUso.MotivoGatewayIndisponivel);
        h.Harness.Published.Select<PagamentoEstornado>().Should().BeEmpty();
        h.Harness.Published.Select<Fault<EstornarPagamento>>().Should().BeEmpty();
        h.Harness.Published.Select<Fault>().Should().BeEmpty();
        repo.Pagamentos[pagamentoId].Status.Should().Be(StatusPagamento.Aprovado);
        h.ChamadasEstorno.Should().Be(4, "entrega inicial + 3 retries; so a ultima vira EstornoFalhou");
    }
}
