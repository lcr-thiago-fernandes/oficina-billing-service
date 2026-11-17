using System.Text.Json.Nodes;
using MassTransit;
using MassTransit.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Infraestrutura.Mensageria;

namespace Oficina.Billing.Infraestrutura.Testes.Mensageria;

public class TopologiaSagaTestes
{
    private static readonly DateTime Agora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<Type> TiposDoBilling()
    {
        var dados = new TheoryData<Type>();
        foreach (var tipo in ContratosSaga.Tipos)
            dados.Add(tipo);
        return dados;
    }

    [Theory, MemberData(nameof(TiposDoBilling))]
    public async Task NomeDoTopico_TemPrefixoDoAmbiente(Type tipo)
    {
        await using var h = await HarnessBilling.IniciarAsync(new RepositorioEmMemoria());

        var topologia = typeof(IBusTopology).GetMethod(nameof(IBusTopology.Message))!
            .MakeGenericMethod(tipo)
            .Invoke(h.Harness.Bus.Topology, null)!;
        var entityName = (string)topologia.GetType().GetProperty("EntityName")!.GetValue(topologia)!;

        // Prova da decisao D1: [EntityName] ignora o formatter global, o prefixo vem de SetEntityName por tipo.
        entityName.Should().Be($"prd-oficina-saga-{tipo.Name}");
    }

    [Fact]
    public void NomeFila_Prd() => TopologiaSaga.NomeFila("prd").Should().Be("prd-oficina-billing-comandos");

    [Fact]
    public void NomeFilaBus_Prd_SufixaInstancia() =>
        TopologiaSaga.NomeFilaBus("prd", "oficina-billing-7d9f8c6b5-x2k4q")
            .Should().Be("prd-oficina-billing-bus-oficina-billing-7d9f8c6b5-x2k4q");

    [Theory]
    [InlineData("MAQUINA-01", "hml-oficina-billing-bus-maquina-01")]
    [InlineData("host.local", "hml-oficina-billing-bus-host-local")]
    [InlineData("pc do joão_1", "hml-oficina-billing-bus-pc-do-jo-o_1")]
    public void NomeFilaBus_SanitizaInstancia(string instancia, string esperado) =>
        TopologiaSaga.NomeFilaBus("hml", instancia).Should().Be(esperado);

    [Fact]
    public void NomeFilaBus_NomeLongo_TruncaEm80()
    {
        var nome = TopologiaSaga.NomeFilaBus("prd", new string('a', 200));

        nome.Should().HaveLength(80);
        nome.Should().StartWith("prd-oficina-billing-bus-aaa");
    }

    [Fact]
    public void InstanciaAtual_UsaHostnameOuNomeDaMaquina()
    {
        var anterior = Environment.GetEnvironmentVariable("HOSTNAME");
        try
        {
            Environment.SetEnvironmentVariable("HOSTNAME", "pod-abc");
            TopologiaSaga.InstanciaAtual().Should().Be("pod-abc");

            Environment.SetEnvironmentVariable("HOSTNAME", null);
            TopologiaSaga.InstanciaAtual().Should().Be(Environment.MachineName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOSTNAME", anterior);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("PRD")]
    [InlineData("hml_1")]
    [InlineData("prd.oficina")]
    [InlineData("prd\n")]
    public void PrefixoAmbienteInvalido_FalhaNaInicializacao(string prefixo)
    {
        var acao = () => new ServiceCollection().AdicionarMensageria(
            new OpcoesMensageria { PrefixoAmbiente = prefixo });

        acao.Should().Throw<InvalidOperationException>().WithMessage("*PrefixoAmbiente*");
    }

    [Fact]
    public void TransporteDesconhecido_FalhaComMensagemClara()
    {
        var acao = () => new ServiceCollection().AdicionarMensageria(new OpcoesMensageria { Transporte = "Kafka" });

        acao.Should().Throw<InvalidOperationException>().WithMessage("*Kafka*AmazonSqs*InMemory*");
    }

    [Fact]
    public void Opcoes_Padrao()
    {
        var o = new OpcoesMensageria();

        OpcoesMensageria.Secao.Should().Be("Mensageria");
        o.Transporte.Should().Be("InMemory");
        o.PrefixoAmbiente.Should().Be("local");
        o.Regiao.Should().Be("us-east-1");
        o.ServiceUrl.Should().BeNull();
        o.IntervaloMinimoRetry.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ConsumidorQueLanca_NaoPublicaFault()
    {
        await using var h = await HarnessBilling.IniciarAsync(new RepositorioQueLanca());
        var osId = Guid.NewGuid();

        await h.EnviarComandoAsync(new GerarOrcamento
        {
            OrdemServicoId = osId,
            OcorridoEm = Agora,
            NumeroOs = 7,
            ClienteDocumento = "52998224725",
            ClienteNome = "Maria",
            Itens = [new ItemGerarOrcamento { Tipo = TipoItemOrcamento.Servico, ReferenciaId = Guid.NewGuid(), Descricao = "Oleo", Quantidade = 1, PrecoUnitario = 10m }],
        });

        (await h.Harness.Consumed.Any<GerarOrcamento>(c => c.Exception is not null)).Should().BeTrue();
        await h.Harness.InactivityTask;
        h.Harness.Published.Select<Fault<GerarOrcamento>>().Should().BeEmpty();
        h.Harness.Published.Select<Fault>().Should().BeEmpty();
        h.Harness.Published.Select<OrcamentoGerado>().Should().BeEmpty();
    }

    [Fact]
    public async Task SerializadorDoBus_UsaEnumComoString()
    {
        await using var h = await HarnessBilling.IniciarAsync(new RepositorioEmMemoria());
        var comando = new GerarOrcamento
        {
            OrdemServicoId = Guid.NewGuid(),
            OcorridoEm = Agora,
            NumeroOs = 7,
            ClienteDocumento = "52998224725",
            ClienteNome = "Maria",
            Itens = [new ItemGerarOrcamento { Tipo = TipoItemOrcamento.Peca, ReferenciaId = Guid.NewGuid(), Descricao = "Filtro", Quantidade = 1, PrecoUnitario = 10m }],
        };

        await h.Harness.Bus.Publish(comando);

        (await h.Harness.Published.Any<GerarOrcamento>()).Should().BeTrue();
        var contexto = h.Harness.Published.Select<GerarOrcamento>().Single().Context;
        var corpo = SystemTextJsonMessageSerializer.Instance.GetMessageBody(contexto);
        var envelope = JsonNode.Parse(corpo.GetBytes())!;
        envelope["message"]!["itens"]![0]!["tipo"]!.GetValue<string>().Should().Be("Peca");
        envelope["message"]!["ocorridoEm"]!.GetValue<string>().Should().Be("2026-11-16T12:00:00Z");
    }
}
