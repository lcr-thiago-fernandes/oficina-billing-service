using Amazon.DynamoDBv2.Model;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;
using Oficina.Billing.Infraestrutura.Persistencia;

namespace Oficina.Billing.Infraestrutura.Testes.Persistencia;

public class RepositorioBillingDynamoTestes : IClassFixture<DynamoFixture>, IAsyncLifetime
{
    private static readonly DateTime Agora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly DynamoFixture _dynamo;
    private string _tabela = null!;
    private RepositorioBillingDynamo _repo = null!;

    public RepositorioBillingDynamoTestes(DynamoFixture dynamo) => _dynamo = dynamo;

    public async Task InitializeAsync()
    {
        var opcoes = await _dynamo.CriarTabelaAsync();
        _tabela = opcoes.Value.NomeTabela;
        _repo = new RepositorioBillingDynamo(_dynamo.Cliente, opcoes, new RelogioFixo());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class RelogioFixo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Agora);
    }

    private static Orcamento NovoOrcamento(Guid? osId = null) =>
        Orcamento.Gerar(osId ?? Guid.NewGuid(), 42, new DadosCliente("529.982.247-25", "Maria", "m@x.com"),
            new[]
            {
                new ItemOrcamento(TipoItem.Servico, Guid.NewGuid(), "Troca de oleo", 1, 91.00m),
                new ItemOrcamento(TipoItem.Peca, Guid.NewGuid(), "Filtro", 2, 50.00m),
            }, Agora);

    private async Task<Orcamento> OrcamentoAprovadoGravado()
    {
        var o = NovoOrcamento();
        (await _repo.InserirOrcamentoAsync(o, default)).Should().BeTrue();
        o.Aprovar("pref-1", "https://pagar/1", Agora);
        await _repo.AtualizarOrcamentoAsync(o, default);
        return (await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default))!;
    }

    private static Pagamento NovoPagamento(Guid osId, string id = "1325123456",
        StatusPagamento status = StatusPagamento.Aprovado) =>
        Pagamento.Registrar(id, osId, 191.00m, "pix", status, null, Agora);

    private static RegistroWebhook Webhook(Guid osId, string id = "1325123456", string evento = "PagamentoConfirmado",
        string? motivo = null) => new(id, osId, evento, motivo, false, Agora);

    private async Task<Dictionary<string, AttributeValue>?> ItemBruto(string pk, string sk)
    {
        var r = await _dynamo.Cliente.GetItemAsync(new GetItemRequest
        {
            TableName = _tabela,
            Key = new() { ["pk"] = new AttributeValue { S = pk }, ["sk"] = new AttributeValue { S = sk } },
            ConsistentRead = true,
        });
        return r.Item is { Count: > 0 } ? r.Item : null;
    }

    [Fact]
    public async Task InserirEObter_Orcamento_PreservaTodosOsCampos()
    {
        var gerado = NovoOrcamento();
        var o = Orcamento.Reconstituir(gerado.Id, gerado.OrdemServicoId, gerado.NumeroOs, gerado.Cliente,
            gerado.Itens, gerado.ValorTotal, StatusOrcamento.Cancelado, "valor_divergente", "pref-1",
            "https://pagar/1", Agora, Agora.AddMinutes(5), 0);

        (await _repo.InserirOrcamentoAsync(o, default)).Should().BeTrue();
        var lido = await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default);

        lido.Should().BeEquivalentTo(o);
        lido!.ValorTotal.Should().Be(191.00m);
        lido.Itens.Should().HaveCount(2);
        lido.CriadoEm.Kind.Should().Be(DateTimeKind.Utc);
        (await _repo.ObterOrcamentoAsync(Guid.NewGuid(), default)).Should().BeNull();
    }

    [Fact]
    public async Task InserirEObter_OrcamentoSemOpcionais_LeNulos()
    {
        var o = Orcamento.Gerar(Guid.NewGuid(), 1, new DadosCliente("52998224725", "Joao", null),
            new[] { new ItemOrcamento(TipoItem.Servico, Guid.NewGuid(), "Revisao", 1, 0.10m) }, Agora);

        await _repo.InserirOrcamentoAsync(o, default);
        var lido = await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default);

        lido.Should().BeEquivalentTo(o);
        var bruto = await ItemBruto($"OS#{o.OrdemServicoId}", "ORCAMENTO");
        bruto!.Keys.Should().NotContain(new[] { "clienteEmail", "motivo", "preferenciaId", "linkPagamento" });
    }

    [Fact]
    public async Task InserirDuplicado_RetornaFalse()
    {
        var o = NovoOrcamento();
        (await _repo.InserirOrcamentoAsync(o, default)).Should().BeTrue();

        var outro = NovoOrcamento(o.OrdemServicoId);
        (await _repo.InserirOrcamentoAsync(outro, default)).Should().BeFalse();
        (await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default))!.Id.Should().Be(o.Id);
    }

    [Fact]
    public async Task Atualizar_ComVersaoCorreta_IncrementaVersao()
    {
        var o = NovoOrcamento();
        await _repo.InserirOrcamentoAsync(o, default);
        var carregado = (await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default))!;
        carregado.Versao.Should().Be(0);

        carregado.Aprovar("pref-1", "https://pagar/1", Agora.AddMinutes(1));
        await _repo.AtualizarOrcamentoAsync(carregado, default);

        carregado.Versao.Should().Be(1);
        var lido = (await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default))!;
        lido.Versao.Should().Be(1);
        lido.Status.Should().Be(StatusOrcamento.Aprovado);
        lido.LinkPagamento.Should().Be("https://pagar/1");
    }

    [Fact]
    public async Task Atualizar_ComVersaoVelha_LancaConcorrencia()
    {
        var o = NovoOrcamento();
        await _repo.InserirOrcamentoAsync(o, default);
        var primeira = (await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default))!;
        var segunda = (await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default))!;

        primeira.Aprovar("pref-1", "https://pagar/1", Agora);
        await _repo.AtualizarOrcamentoAsync(primeira, default);
        segunda.Rejeitar("caro", Agora);
        var acao = () => _repo.AtualizarOrcamentoAsync(segunda, default);

        await acao.Should().ThrowAsync<ConcorrenciaException>();
        segunda.Versao.Should().Be(0);
        (await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default))!.Status.Should().Be(StatusOrcamento.Aprovado);
    }

    [Fact]
    public async Task SalvarPagamento_NovoDuplicado_LancaConcorrencia()
    {
        var osId = Guid.NewGuid();
        await _repo.SalvarPagamentoAsync(NovoPagamento(osId), novo: true, default);

        var acao = () => _repo.SalvarPagamentoAsync(NovoPagamento(osId), novo: true, default);

        await acao.Should().ThrowAsync<ConcorrenciaException>();
    }

    [Fact]
    public async Task SalvarPagamento_Existente_GravaComVersaoEPreservaCampos()
    {
        var osId = Guid.NewGuid();
        await _repo.SalvarPagamentoAsync(NovoPagamento(osId), novo: true, default);
        var carregado = (await _repo.ListarPagamentosAsync(osId, default)).Single();
        var velho = (await _repo.ListarPagamentosAsync(osId, default)).Single();

        carregado.Estornar("est-9", Agora.AddMinutes(3));
        await _repo.SalvarPagamentoAsync(carregado, novo: false, default);

        carregado.Versao.Should().Be(1);
        var lido = (await _repo.ListarPagamentosAsync(osId, default)).Single();
        lido.Should().BeEquivalentTo(carregado);
        lido.Valor.Should().Be(191.00m);

        velho.Estornar("est-10", Agora);
        var acao = () => _repo.SalvarPagamentoAsync(velho, novo: false, default);
        await acao.Should().ThrowAsync<ConcorrenciaException>();
    }

    [Fact]
    public async Task ListarPagamentos_RetornaSomenteDaOs()
    {
        var o = NovoOrcamento();
        await _repo.InserirOrcamentoAsync(o, default);
        var osId = o.OrdemServicoId;
        await _repo.SalvarPagamentoAsync(NovoPagamento(osId, "1"), novo: true, default);
        await _repo.SalvarPagamentoAsync(NovoPagamento(osId, "2", StatusPagamento.Recusado), novo: true, default);
        await _repo.SalvarPagamentoAsync(NovoPagamento(Guid.NewGuid(), "3"), novo: true, default);

        var pagamentos = await _repo.ListarPagamentosAsync(osId, default);

        pagamentos.Select(p => p.Id).Should().BeEquivalentTo(new[] { "1", "2" });
        pagamentos.Should().OnlyContain(p => p.OrdemServicoId == osId);
        (await _repo.ListarPagamentosAsync(Guid.NewGuid(), default)).Should().BeEmpty();
    }

    [Fact]
    public async Task RegistrarDesfecho_GravaWebhookPagamentoEOrcamentoAtomicamente()
    {
        var o = await OrcamentoAprovadoGravado();
        var osId = o.OrdemServicoId;
        await _repo.SalvarPagamentoAsync(NovoPagamento(osId, status: StatusPagamento.Pendente), novo: true, default);
        var pagamento = (await _repo.ListarPagamentosAsync(osId, default)).Single();
        pagamento.Recusar("cc_rejected_other_reason", Agora);
        o.Cancelar("cc_rejected_other_reason", Agora);

        var gravou = await _repo.RegistrarDesfechoAsync(new DesfechoPagamento(
            Webhook(osId, evento: "PagamentoRecusado", motivo: "cc_rejected_other_reason"), pagamento, false, o), default);

        gravou.Should().BeTrue();
        pagamento.Versao.Should().Be(1);
        o.Versao.Should().Be(2);
        var registro = await _repo.ObterRegistroWebhookAsync("1325123456", default);
        registro.Should().Be(new RegistroWebhook("1325123456", osId, "PagamentoRecusado", "cc_rejected_other_reason",
            false, Agora));
        var lidoPagamento = (await _repo.ListarPagamentosAsync(osId, default)).Single();
        lidoPagamento.Status.Should().Be(StatusPagamento.Recusado);
        lidoPagamento.DetalheStatus.Should().Be("cc_rejected_other_reason");
        lidoPagamento.Versao.Should().Be(1);
        var lidoOrcamento = (await _repo.ObterOrcamentoAsync(osId, default))!;
        lidoOrcamento.Status.Should().Be(StatusOrcamento.Cancelado);
        lidoOrcamento.Versao.Should().Be(2);

        var bruto = (await ItemBruto("WEBHOOK#1325123456", "WEBHOOK"))!;
        bruto["expiraEm"].N.Should().Be(new DateTimeOffset(Agora).AddDays(90).ToUnixTimeSeconds().ToString());
        bruto["eventoPublicado"].BOOL.Should().BeFalse();
    }

    [Fact]
    public async Task RegistrarDesfecho_SemOrcamento_GravaPagamentoNovoEWebhook()
    {
        var osId = Guid.NewGuid();
        var pagamento = NovoPagamento(osId, status: StatusPagamento.Recusado);

        (await _repo.RegistrarDesfechoAsync(new DesfechoPagamento(Webhook(osId, evento: "Nenhum"), pagamento, true, null),
            default)).Should().BeTrue();

        pagamento.Versao.Should().Be(0);
        (await _repo.ListarPagamentosAsync(osId, default)).Single().Status.Should().Be(StatusPagamento.Recusado);
        (await _repo.ObterRegistroWebhookAsync("1325123456", default))!.EventoTipo.Should().Be("Nenhum");
        (await _repo.ObterRegistroWebhookAsync("outro", default)).Should().BeNull();
    }

    [Fact]
    public async Task RegistrarDesfecho_Duplicado_RetornaFalse()
    {
        var osId = Guid.NewGuid();
        (await _repo.RegistrarDesfechoAsync(new DesfechoPagamento(Webhook(osId), NovoPagamento(osId), true, null),
            default)).Should().BeTrue();

        var segundo = await _repo.RegistrarDesfechoAsync(
            new DesfechoPagamento(Webhook(osId, evento: "PagamentoRecusado"), NovoPagamento(osId), true, null), default);

        segundo.Should().BeFalse();
        (await _repo.ListarPagamentosAsync(osId, default)).Should().ContainSingle();
        (await _repo.ObterRegistroWebhookAsync("1325123456", default))!.EventoTipo.Should().Be("PagamentoConfirmado");
    }

    [Fact]
    public async Task RegistrarDesfecho_OrcamentoComVersaoVelha_NaoGravaNada()
    {
        var o = await OrcamentoAprovadoGravado();
        var velho = (await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default))!;
        o.Cancelar("saga", Agora);
        await _repo.AtualizarOrcamentoAsync(o, default);
        velho.Cancelar("valor_divergente", Agora);
        var pagamento = NovoPagamento(o.OrdemServicoId, status: StatusPagamento.Recusado);

        var acao = () => _repo.RegistrarDesfechoAsync(new DesfechoPagamento(
            Webhook(o.OrdemServicoId, evento: "PagamentoRecusado"), pagamento, true, velho), default);

        await acao.Should().ThrowAsync<ConcorrenciaException>();
        (await _repo.ObterRegistroWebhookAsync("1325123456", default)).Should().BeNull();
        (await _repo.ListarPagamentosAsync(o.OrdemServicoId, default)).Should().BeEmpty();
        (await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default))!.Motivo.Should().Be("saga");
        velho.Versao.Should().Be(1);
    }

    [Fact]
    public async Task RegistrarDesfecho_PagamentoComVersaoVelha_LancaConcorrencia()
    {
        var osId = Guid.NewGuid();
        await _repo.SalvarPagamentoAsync(NovoPagamento(osId, status: StatusPagamento.Pendente), novo: true, default);
        var velho = (await _repo.ListarPagamentosAsync(osId, default)).Single();
        var atual = (await _repo.ListarPagamentosAsync(osId, default)).Single();
        atual.Recusar(null, Agora);
        await _repo.SalvarPagamentoAsync(atual, novo: false, default);
        velho.Aprovar(Agora);

        var acao = () => _repo.RegistrarDesfechoAsync(new DesfechoPagamento(Webhook(osId), velho, false, null), default);

        await acao.Should().ThrowAsync<ConcorrenciaException>();
        (await _repo.ObterRegistroWebhookAsync("1325123456", default)).Should().BeNull();
    }

    [Fact]
    public async Task RegistrarDesfecho_DuasConfirmacoesSimultaneasDaMesmaOs_SoAPrimeiraGrava()
    {
        // Os dois webhooks leram o mesmo orcamento (mesma versao) e nenhum pagamento aprovado: o orcamento entra na
        // transacao como guarda de versao, e a segunda confirmacao falha em vez de confirmar a OS duas vezes.
        var o = await OrcamentoAprovadoGravado();
        var leituraA = (await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default))!;
        var leituraB = (await _repo.ObterOrcamentoAsync(o.OrdemServicoId, default))!;

        (await _repo.RegistrarDesfechoAsync(new DesfechoPagamento(Webhook(o.OrdemServicoId, "A"),
            NovoPagamento(o.OrdemServicoId, "A"), true, leituraA), default)).Should().BeTrue();
        var segunda = () => _repo.RegistrarDesfechoAsync(new DesfechoPagamento(Webhook(o.OrdemServicoId, "B"),
            NovoPagamento(o.OrdemServicoId, "B"), true, leituraB), default);

        await segunda.Should().ThrowAsync<ConcorrenciaException>();
        (await _repo.ListarPagamentosAsync(o.OrdemServicoId, default)).Select(p => p.Id).Should().Equal("A");
        (await _repo.ObterRegistroWebhookAsync("B", default)).Should().BeNull();
    }

    [Fact]
    public async Task MarcarEventoPublicado_AtualizaFlag()
    {
        var osId = Guid.NewGuid();
        await _repo.RegistrarDesfechoAsync(new DesfechoPagamento(Webhook(osId), NovoPagamento(osId), true, null), default);

        await _repo.MarcarEventoPublicadoAsync("1325123456", default);

        var registro = (await _repo.ObterRegistroWebhookAsync("1325123456", default))!;
        registro.EventoPublicado.Should().BeTrue();
        registro.EventoTipo.Should().Be("PagamentoConfirmado");
        (await ItemBruto("WEBHOOK#1325123456", "WEBHOOK"))!.Should().ContainKey("expiraEm");
    }
}
