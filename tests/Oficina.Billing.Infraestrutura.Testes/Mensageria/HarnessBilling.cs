using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Oficina.Billing.Aplicacao;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Infraestrutura.Mensageria;
using Oficina.Billing.Infraestrutura.Simulador;

namespace Oficina.Billing.Infraestrutura.Testes.Mensageria;

/// <summary>
/// Harness InMemory com a mesma configuracao de bus da producao (<see cref="ConfiguracaoMensageria.ConfigurarRegistro"/>):
/// no 8.5.12 o <c>AddMassTransitTestHarness</c> substitui o registro, por isso recebe o mesmo configurador.
/// </summary>
internal sealed class HarnessBilling : IAsyncDisposable
{
    public const string Prefixo = "prd";

    private readonly ServiceProvider _provider;

    private readonly GatewayContador _contador;

    private HarnessBilling(ServiceProvider provider, ITestHarness harness, GatewayPagamentoSimulado gateway,
        GatewayContador contador)
    {
        _provider = provider;
        Harness = harness;
        Gateway = gateway;
        _contador = contador;
    }

    public ITestHarness Harness { get; }
    public GatewayPagamentoSimulado Gateway { get; }

    /// <summary>Quantas vezes o consumidor chamou o estorno no gateway (entrega inicial + retries).</summary>
    public int ChamadasEstorno => _contador.ChamadasEstorno;

    public static async Task<HarnessBilling> IniciarAsync(RepositorioEmMemoria repositorio,
        bool simularFalhaEstorno = false)
    {
        var servicos = new ServiceCollection();
        servicos.AddLogging();
        servicos.AdicionarAplicacao();
        servicos.AddSingleton<IRepositorioBilling>(repositorio);
        servicos.AddSingleton(Options.Create(new OpcoesPagamento { SimularFalhaEstorno = simularFalhaEstorno }));
        servicos.AddSingleton<GatewayPagamentoSimulado>();
        servicos.AddSingleton<GatewayContador>();
        servicos.AddSingleton<IGatewayPagamento>(sp => sp.GetRequiredService<GatewayContador>());
        servicos.AddSingleton(Mock.Of<ITelemetriaPagamento>());
        servicos.AddScoped<IPublicadorMensagens, PublicadorMassTransit>();
        servicos.AddMassTransitTestHarness(x => ConfiguracaoMensageria.ConfigurarRegistro(x, new OpcoesMensageria
        {
            Transporte = "InMemory",
            PrefixoAmbiente = Prefixo,
            IntervaloMinimoRetry = TimeSpan.FromMilliseconds(10),
        }));

        var provider = servicos.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        harness.TestTimeout = TimeSpan.FromSeconds(30);
        await harness.Start();
        return new HarnessBilling(provider, harness, provider.GetRequiredService<GatewayPagamentoSimulado>(),
            provider.GetRequiredService<GatewayContador>());
    }

    public async Task EnviarComandoAsync<T>(T comando) where T : class =>
        await (await Harness.Bus.GetSendEndpoint(new Uri($"queue:{TopologiaSaga.NomeFila(Prefixo)}"))).Send(comando);

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    /// <summary>Decorador do gateway simulado que conta as chamadas de estorno.</summary>
    internal sealed class GatewayContador(GatewayPagamentoSimulado interno) : IGatewayPagamento
    {
        private int _chamadasEstorno;

        public int ChamadasEstorno => Volatile.Read(ref _chamadasEstorno);

        public string Nome => interno.Nome;

        public Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken ct) =>
            interno.CriarCobrancaAsync(cobranca, ct);

        public Task<PagamentoGateway?> ObterPagamentoAsync(string pagamentoId, CancellationToken ct) =>
            interno.ObterPagamentoAsync(pagamentoId, ct);

        public Task<EstornoGateway> EstornarAsync(string pagamentoId, decimal valor, string chaveIdempotencia,
            CancellationToken ct)
        {
            Interlocked.Increment(ref _chamadasEstorno);
            return interno.EstornarAsync(pagamentoId, valor, chaveIdempotencia, ct);
        }
    }
}
