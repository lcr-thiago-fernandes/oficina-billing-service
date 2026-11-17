using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit.Testing;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Bdd.Testes.Suporte;
using Reqnroll;

namespace Oficina.Billing.Bdd.Testes.Passos;

[Binding]
public sealed class FluxoPagamentoOrcamentoPassos(AmbienteBdd ambiente)
{
    private const string Base = "/billing/api/v1";
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(15);

    private string _cpf = "";
    private string? _motivoRejeicao;
    private Guid OsId => ambiente.OrdemServicoId;
    private ITestHarness Harness => ambiente.Fabrica.Harness;

    private static decimal Valor(string texto) => decimal.Parse(texto, CultureInfo.InvariantCulture);

    private static CancellationToken Token() => new CancellationTokenSource(Limite).Token;

    [Given(@"^que o OS Service solicitou o orçamento da OS (\d+) do cliente ""(.*)"" com os itens:$")]
    public async Task DadoOrcamentoSolicitado(int numeroOs, string cpf, DataTable itens)
    {
        _cpf = cpf;
        await EnviarComandoAsync(new GerarOrcamento
        {
            OrdemServicoId = OsId,
            OcorridoEm = DateTime.UtcNow,
            NumeroOs = numeroOs,
            ClienteDocumento = cpf,
            ClienteNome = "Maria da Silva",
            ClienteEmail = "maria@exemplo.com",
            Itens = itens.Rows.Select(l => new ItemGerarOrcamento
            {
                Tipo = Enum.Parse<TipoItemOrcamento>(l["tipo"]),
                ReferenciaId = Guid.NewGuid(),
                Descricao = l["descricao"],
                Quantidade = int.Parse(l["quantidade"], CultureInfo.InvariantCulture),
                PrecoUnitario = Valor(l["precoUnitario"]),
            }).ToList(),
        });
        (await Harness.Consumed.Any<GerarOrcamento>(x => x.Context.Message.OrdemServicoId == OsId, Token()))
            .Should().BeTrue("o Billing deve consumir o comando GerarOrcamento");
    }

    [When(@"^o cliente ""(.*)"" aprova o orçamento$")]
    public async Task QuandoClienteAprova(string cpf)
    {
        var resposta = await ambiente.Fabrica.Cliente("Cliente", cpf).PostAsync($"{Base}/orcamentos/{OsId}/aprovacao", null);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
    }

    [When(@"^o cliente ""(.*)"" rejeita o orçamento com motivo ""(.*)""$")]
    public async Task QuandoClienteRejeita(string cpf, string motivo)
    {
        _motivoRejeicao = motivo;
        var resposta = await ambiente.Fabrica.Cliente("Cliente", cpf)
            .PostAsJsonAsync($"{Base}/orcamentos/{OsId}/rejeicao", new { motivo });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
    }

    [When(@"^o pagamento é ""(aprovado|recusado)"" no gateway$")]
    public async Task QuandoPagamentoNoGateway(string resultado)
    {
        var resposta = await ambiente.Fabrica.Cliente("Admin")
            .PostAsJsonAsync($"{Base}/simulador/pagamentos/{OsId}", new { resultado });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
    }

    [When(@"^o OS Service solicita o estorno com motivo ""(.*)""$")]
    public async Task QuandoSolicitaEstorno(string motivo)
    {
        // A confirmacao do pagamento precisa ter sido publicada antes do estorno (o comando e assincrono).
        (await Harness.Published.Any<PagamentoConfirmado>(x => x.Context.Message.OrdemServicoId == OsId, Token()))
            .Should().BeTrue();
        await EnviarComandoAsync(new EstornarPagamento { OrdemServicoId = OsId, OcorridoEm = DateTime.UtcNow, Motivo = motivo });
        (await Harness.Consumed.Any<EstornarPagamento>(x => x.Context.Message.OrdemServicoId == OsId, Token()))
            .Should().BeTrue("o Billing deve consumir o comando EstornarPagamento");
    }

    [Then(@"^o evento ""(\w+)"" é publicado com valor total (\d+\.\d+)$")]
    public async Task EntaoEventoComValorTotal(string evento, string valor) =>
        (await PublicadoAsync<OrcamentoGerado>(evento, m => m.ValorTotal == Valor(valor)))
            .Should().BeTrue($"{evento} deve ter valor total {valor}");

    [Then(@"^o evento ""(\w+)"" é publicado com valor (\d+\.\d+)$")]
    public async Task EntaoEventoComValor(string evento, string valor)
    {
        var esperado = Valor(valor);
        var publicado = evento switch
        {
            nameof(PagamentoConfirmado) => await PublicadoAsync<PagamentoConfirmado>(evento, m => m.Valor == esperado),
            nameof(PagamentoEstornado) => await PublicadoAsync<PagamentoEstornado>(evento, m => m.Valor == esperado),
            _ => throw new PendingStepException(),
        };
        publicado.Should().BeTrue($"{evento} deve ter valor {valor}");
    }

    [Then(@"^o evento ""(\w+)"" é publicado com link de pagamento$")]
    public async Task EntaoEventoComLink(string evento) =>
        (await PublicadoAsync<OrcamentoAprovado>(evento, m => !string.IsNullOrWhiteSpace(m.LinkPagamento)))
            .Should().BeTrue($"{evento} deve trazer o link de pagamento");

    [Then(@"^o evento ""(\w+)"" é publicado$")]
    public async Task EntaoEventoPublicado(string evento)
    {
        var publicado = evento switch
        {
            nameof(PagamentoRecusado) => await PublicadoAsync<PagamentoRecusado>(evento, m => !string.IsNullOrWhiteSpace(m.Motivo)),
            nameof(OrcamentoRejeitado) => await PublicadoAsync<OrcamentoRejeitado>(evento, m => m.Motivo == _motivoRejeicao && m.OrcamentoId != Guid.Empty),
            _ => throw new PendingStepException(),
        };
        publicado.Should().BeTrue($"{evento} deve ser publicado com os campos do contrato");
    }

    [Then(@"^nenhum evento ""PagamentoConfirmado"" é publicado$")]
    public void EntaoNenhumPagamentoConfirmado() =>
        Harness.Published.Select<PagamentoConfirmado>(x => x.Context.Message.OrdemServicoId == OsId).Should().BeEmpty();

    [Then(@"^o pagamento da OS fica com status ""(.*)""$")]
    public async Task EntaoStatusPagamento(string status)
    {
        var json = await ObterAsync($"{Base}/pagamentos/{OsId}");
        json.EnumerateArray().Select(p => p.GetProperty("status").GetString()).Should().ContainSingle().Which.Should().Be(status);
    }

    [Then(@"^o orçamento fica com status ""(.*)""$")]
    public async Task EntaoStatusOrcamento(string status)
    {
        var json = await ObterAsync($"{Base}/orcamentos/{OsId}");
        json.GetProperty("status").GetString().Should().Be(status);
    }

    private async Task EnviarComandoAsync<T>(T comando) where T : class
    {
        var destino = await Harness.Bus.GetSendEndpoint(new Uri($"queue:{ambiente.Fabrica.FilaComandos}"));
        await destino.Send(comando);
    }

    /// <summary>Espera (ate o limite) o evento do tipo <typeparamref name="T"/> da OS do cenario que atenda ao filtro.</summary>
    private async Task<bool> PublicadoAsync<T>(string nomeEsperado, Func<T, bool> filtro) where T : class
    {
        typeof(T).Name.Should().Be(nomeEsperado, "o nome do evento no Gherkin deve corresponder ao contrato");
        return await Harness.Published.Any<T>(x => x.Context.Message is IMensagemSaga m && m.OrdemServicoId == OsId
            && filtro(x.Context.Message), Token());
    }

    private async Task<JsonElement> ObterAsync(string rota)
    {
        var resposta = await ambiente.Fabrica.Cliente("Cliente", _cpf).GetAsync(rota);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
