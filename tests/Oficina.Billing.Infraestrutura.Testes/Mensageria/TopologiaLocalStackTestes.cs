using System.Collections.Concurrent;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oficina.Billing.Aplicacao;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Dominio.Pagamentos;
using Oficina.Billing.Infraestrutura.Mensageria;
using Testcontainers.LocalStack;

namespace Oficina.Billing.Infraestrutura.Testes.Mensageria;

/// <summary>
/// Sobe o bus real (Transporte=AmazonSqs, composicao de producao via <c>AdicionarInfraestrutura</c>) contra o
/// LocalStack e confere os nomes finais no SNS/SQS. O repositorio e trocado por um que sempre lanca.
/// </summary>
public sealed class LocalStackFixture : IAsyncLifetime
{
    public const string Prefixo = "test";

    private readonly LocalStackContainer _container = new LocalStackBuilder("localstack/localstack:3.8.1")
        .WithEnvironment("SERVICES", "sqs,sns")
        .Build();

    private ServiceProvider? _provider;

    public IBusControl Bus { get; private set; } = null!;
    public IAmazonSimpleNotificationService Sns { get; private set; } = null!;
    public IAmazonSQS Sqs { get; private set; } = null!;
    internal RepositorioQueLanca Repositorio { get; } = new();
    internal GatewayFalhaTransitoria Gateway { get; } = new();
    internal PublicadorGravador Publicador { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var serviceUrl = _container.GetConnectionString();

        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mensageria:Transporte"] = "AmazonSqs",
                ["Mensageria:PrefixoAmbiente"] = Prefixo,
                ["Mensageria:ServiceUrl"] = serviceUrl,
                // Retry e redelivery encurtados (1 s / 5 s / 15 s) para a falha chegar a _error no teste.
                ["Mensageria:IntervaloMinimoRetry"] = "00:00:00.010",
                ["Mensageria:IntervaloBaseRedelivery"] = "00:00:01",
                // DynamoDB nao e exercitado (repositorio substituido abaixo); o cliente so precisa ser construido.
                ["Dynamo:ServiceUrl"] = serviceUrl,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AdicionarAplicacao();
        services.AdicionarInfraestrutura(configuracao);
        services.AddSingleton<IRepositorioBilling>(Repositorio);
        // Estorno: gateway que falha nas N primeiras chamadas e publicador que grava o desfecho (sem SNS).
        services.AddSingleton<IGatewayPagamento>(Gateway);
        services.AddScoped<IPublicadorMensagens>(_ => Publicador);
        _provider = services.BuildServiceProvider(true);

        Bus = _provider.GetRequiredService<IBusControl>();
        await Bus.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        var credenciais = new BasicAWSCredentials("test", "test");
        Sns = new AmazonSimpleNotificationServiceClient(credenciais,
            new AmazonSimpleNotificationServiceConfig { ServiceURL = serviceUrl, AuthenticationRegion = "us-east-1" });
        Sqs = new AmazonSQSClient(credenciais,
            new AmazonSQSConfig { ServiceURL = serviceUrl, AuthenticationRegion = "us-east-1" });
    }

    public async Task DisposeAsync()
    {
        if (Bus is not null)
            await Bus.StopAsync();
        if (_provider is not null)
            await _provider.DisposeAsync();
        Sns?.Dispose();
        Sqs?.Dispose();
        await _container.DisposeAsync();
    }

    public async Task<List<string>> ListarTopicosAsync()
    {
        var arns = new List<string>();
        string? proximo = null;
        do
        {
            var r = await Sns.ListTopicsAsync(new ListTopicsRequest { NextToken = proximo });
            arns.AddRange((r.Topics ?? []).Select(t => t.TopicArn));
            proximo = r.NextToken;
        } while (!string.IsNullOrEmpty(proximo));
        return arns;
    }

    public async Task<List<string>> ListarFilasAsync() =>
        (await Sqs.ListQueuesAsync(new ListQueuesRequest())).QueueUrls ?? [];

    public async Task<string> AguardarFilaAsync(string nome, TimeSpan limite)
    {
        var fim = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < fim)
        {
            var url = (await ListarFilasAsync()).FirstOrDefault(u => u.EndsWith("/" + nome));
            if (url is not null)
                return url;
            await Task.Delay(500);
        }

        throw new TimeoutException($"A fila {nome} nao foi criada em {limite}.");
    }
}

[Collection(ColecaoAmbiente.Nome)]
public sealed class TopologiaLocalStackTestes(LocalStackFixture fx) : IClassFixture<LocalStackFixture>
{
    private static readonly DateTime Agora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static string NomeFinal(string arnOuUrl, char separador) =>
        arnOuUrl[(arnOuUrl.LastIndexOf(separador) + 1)..];

    [Fact]
    public async Task AmazonSqs_PublicarCadaContrato_CriaTopicoComPrefixo()
    {
        foreach (var tipo in ContratosSaga.Tipos)
            await fx.Bus.Publish(Activator.CreateInstance(tipo)!, tipo);

        var topicos = (await fx.ListarTopicosAsync()).Select(t => NomeFinal(t, ':')).ToList();

        topicos.Should().Contain(ContratosSaga.Tipos.Select(t => $"test-oficina-saga-{t.Name}"));
        topicos.Should().HaveCount(11);
    }

    [Fact]
    public async Task AmazonSqs_CriaFilaDeComandos()
    {
        var filas = await fx.ListarFilasAsync();

        filas.Should().Contain(u => u.EndsWith("/test-oficina-billing-comandos"));
    }

    [Fact]
    public async Task AmazonSqs_FilaDoBusPorInstanciaETodasAsFilasComPrefixo()
    {
        // A fila do bus e criada sob demanda; conectar um handler forca a criacao para conferir o nome real no SQS.
        var filaBus = TopologiaSaga.NomeFilaBus(LocalStackFixture.Prefixo, TopologiaSaga.InstanciaAtual());
        filaBus.Should().StartWith("test-oficina-billing-bus-");
        fx.Bus.Address.AbsolutePath.Should().Be("/" + filaBus);
        var handle = fx.Bus.ConnectHandler<OrcamentoGerado>(_ => Task.CompletedTask);
        try
        {
            await fx.AguardarFilaAsync(filaBus, TimeSpan.FromSeconds(30));
        }
        finally
        {
            handle.Disconnect();
        }

        var nomes = (await fx.ListarFilasAsync()).Select(u => NomeFinal(u, '/')).ToList();

        nomes.Should().Contain(filaBus);
        nomes.Should().OnlyContain(n => n.StartsWith("test-oficina-"));
    }

    [Fact]
    public async Task AmazonSqs_ConsumidorFalhando_PassaPeloRedeliveryTerminaNaErrorSemTopicoFault()
    {
        var comando = new GerarOrcamento
        {
            OrdemServicoId = Guid.NewGuid(),
            OcorridoEm = Agora,
            NumeroOs = 7,
            ClienteDocumento = "52998224725",
            ClienteNome = "Maria",
            Itens = [new ItemGerarOrcamento { Tipo = TipoItemOrcamento.Servico, ReferenciaId = Guid.NewGuid(), Descricao = "Oleo", Quantidade = 1, PrecoUnitario = 10m }],
        };
        await (await fx.Bus.GetSendEndpoint(new Uri("queue:test-oficina-billing-comandos"))).Send(comando);

        // Redelivery 1 s + 5 s + 15 s (escala do teste) antes de ir para a _error.
        var urlErro = await fx.AguardarFilaAsync("test-oficina-billing-comandos_error", TimeSpan.FromSeconds(120));
        var recebida = await fx.Sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = urlErro,
            WaitTimeSeconds = 10,
            MaxNumberOfMessages = 1,
        });
        (recebida.Messages ?? []).Should().ContainSingle("a falha definitiva deve terminar na fila _error");

        // (1 entrega + 3 retries) x (1 entrega + 3 redeliveries atrasados): prova o UseDelayedRedelivery no SQS.
        fx.Repositorio.Tentativas.Should().Be(16);

        var topicos = (await fx.ListarTopicosAsync()).Select(t => NomeFinal(t, ':')).ToList();
        // Pode estar vazia se este teste rodar antes do de publicacao: nenhum topico fora do padrao, nenhum Fault.
        topicos.Should().NotContain(t => !t.StartsWith("test-oficina-saga-"));
        topicos.Should().NotContain(t => t.Contains("Fault"));
    }

    [Fact]
    public async Task AmazonSqs_EstornoComGatewayForaNaPrimeiraEntrega_EstornaNoRedeliverySemEstornoFalhou()
    {
        // Antes: o contador de retry reinicia a cada redelivery e GetRetryAttempt() >= 3 encerrava ja na 1a entrega
        // (~20 s de indisponibilidade viravam EstornoFalhou terminal). Agora so o ultimo retry da ultima redelivery.
        var osId = Guid.NewGuid();
        var pagamentoId = $"mp-{Guid.NewGuid():N}";
        fx.Repositorio.Pagamentos[pagamentoId] = Pagamento.Registrar(pagamentoId, osId, 191.00m, "pix",
            StatusPagamento.Aprovado, null, Agora);
        fx.Gateway.FalharNasPrimeiras(pagamentoId, 4); // entrega inicial + 3 retries imediatos

        await (await fx.Bus.GetSendEndpoint(new Uri("queue:test-oficina-billing-comandos")))
            .Send(new EstornarPagamento { OrdemServicoId = osId, OcorridoEm = Agora, Motivo = "Falha na execucao" });

        var fim = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < fim && !fx.Publicador.Publicadas.Any(m => Da(m, osId)))
            await Task.Delay(200);

        var desfechos = fx.Publicador.Publicadas.Where(m => Da(m, osId)).ToList();
        desfechos.Should().ContainSingle().Which.Should().BeOfType<PagamentoEstornado>()
            .Which.PagamentoId.Should().Be(pagamentoId);
        fx.Gateway.Chamadas(pagamentoId).Should().Be(5, "4 falhas na 1a entrega + sucesso no 1o redelivery");
        fx.Repositorio.Pagamentos[pagamentoId].Status.Should().Be(StatusPagamento.Estornado);
    }

    private static bool Da(object mensagem, Guid osId) => mensagem switch
    {
        PagamentoEstornado e => e.OrdemServicoId == osId,
        EstornoFalhou f => f.OrdemServicoId == osId,
        _ => false,
    };
}

/// <summary>Gateway que lanca indisponibilidade nas N primeiras chamadas de estorno de um pagamento.</summary>
internal sealed class GatewayFalhaTransitoria : IGatewayPagamento
{
    private readonly ConcurrentDictionary<string, int> _falhas = new();
    private readonly ConcurrentDictionary<string, int> _chamadas = new();

    public string Nome => "Teste";

    public void FalharNasPrimeiras(string pagamentoId, int vezes) => _falhas[pagamentoId] = vezes;

    public int Chamadas(string pagamentoId) => _chamadas.GetValueOrDefault(pagamentoId);

    public Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<PagamentoGateway?> ObterPagamentoAsync(string pagamentoId, CancellationToken ct) =>
        Task.FromResult<PagamentoGateway?>(null);

    public Task<EstornoGateway> EstornarAsync(string pagamentoId, decimal valor, string chaveIdempotencia,
        CancellationToken ct)
    {
        var chamada = _chamadas.AddOrUpdate(pagamentoId, 1, (_, n) => n + 1);
        if (chamada <= _falhas.GetValueOrDefault(pagamentoId))
            throw new GatewayPagamentoIndisponivelException("indisponivel (teste)");
        return Task.FromResult(new EstornoGateway($"est-{pagamentoId}", valor));
    }
}

/// <summary>Grava as mensagens publicadas pelos casos de uso (sem passar pelo SNS).</summary>
internal sealed class PublicadorGravador : IPublicadorMensagens
{
    public ConcurrentQueue<object> Publicadas { get; } = new();

    public Task PublicarAsync<T>(T mensagem, CancellationToken ct) where T : class
    {
        Publicadas.Enqueue(mensagem);
        return Task.CompletedTask;
    }
}
