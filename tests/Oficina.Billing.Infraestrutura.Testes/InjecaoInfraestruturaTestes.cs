using Amazon.DynamoDBv2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Oficina.Billing.Aplicacao;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Infraestrutura.Mensageria;
using Oficina.Billing.Infraestrutura.MercadoPago;
using Oficina.Billing.Infraestrutura.Persistencia;
using Oficina.Billing.Infraestrutura.Simulador;
using Oficina.Billing.Infraestrutura.Telemetria;

namespace Oficina.Billing.Infraestrutura.Testes;

public class InjecaoInfraestruturaTestes
{
    private static ServiceCollection Registrar(Dictionary<string, string?> valores)
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var servicos = new ServiceCollection();
        servicos.AddLogging();
        servicos.AdicionarAplicacao();
        servicos.AdicionarInfraestrutura(configuracao);
        return servicos;
    }

    [Fact]
    public void ComServiceUrl_ResolveDynamoSemCredenciaisAws()
    {
        using var provider = Registrar(new() { ["Dynamo:ServiceUrl"] = "http://localhost:8000" })
            .BuildServiceProvider();

        var cliente = provider.GetRequiredService<IAmazonDynamoDB>();

        cliente.Config.ServiceURL.Should().StartWith("http://localhost:8000");
        cliente.Config.AuthenticationRegion.Should().Be("us-east-1");
    }

    [Fact]
    public void Inicializador_SoRegistradoComFlag()
    {
        static bool TemInicializador(IServiceCollection s) =>
            s.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(InicializadorTabelaDynamo));

        TemInicializador(Registrar(new() { ["Dynamo:CriarTabelaSeAusente"] = "false" })).Should().BeFalse();
        TemInicializador(Registrar(new())).Should().BeFalse();
        TemInicializador(Registrar(new() { ["Dynamo:CriarTabelaSeAusente"] = "true" })).Should().BeTrue();
    }

    [Fact]
    public void ModoSimulado_GatewaySimuladoSingletonUnico()
    {
        using var provider = Registrar(new() { ["Pagamento:Modo"] = "Simulado" }).BuildServiceProvider();

        var gateway = provider.GetRequiredService<IGatewayPagamento>();

        gateway.Should().BeOfType<GatewayPagamentoSimulado>();
        gateway.Should().BeSameAs(provider.GetRequiredService<GatewayPagamentoSimulado>());
        gateway.Should().BeSameAs(provider.GetRequiredService<IGatewayPagamento>());
    }

    [Fact]
    public void SemModo_PadraoSimulado()
    {
        using var provider = Registrar(new()).BuildServiceProvider();

        provider.GetRequiredService<IGatewayPagamento>().Should().BeOfType<GatewayPagamentoSimulado>();
    }

    [Fact]
    public void ModoMercadoPago_GatewayRealComTimeoutDe10s()
    {
        using var provider = Registrar(new() { ["Pagamento:Modo"] = "MercadoPago" }).BuildServiceProvider();

        provider.GetRequiredService<IGatewayPagamento>().Should().BeOfType<MercadoPagoGateway>();
        provider.GetService<GatewayPagamentoSimulado>().Should().BeNull();
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IGatewayPagamento))
            .Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void RegistraRepositorioTelemetriaEPublicador()
    {
        var servicos = Registrar(new());
        using var provider = servicos.BuildServiceProvider();

        provider.GetRequiredService<IRepositorioBilling>().Should().BeOfType<RepositorioBillingDynamo>();
        provider.GetRequiredService<ITelemetriaPagamento>().Should().BeOfType<TelemetriaPagamentoNewRelic>();
        servicos.Should().ContainSingle(d => d.ServiceType == typeof(IPublicadorMensagens))
            .Which.Should().Match<ServiceDescriptor>(d =>
                d.ImplementationType == typeof(PublicadorMassTransit) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void PrefixoInvalidoNaConfiguracao_FalhaNaInicializacao()
    {
        var acao = () => Registrar(new() { ["Mensageria:PrefixoAmbiente"] = "Prd" });

        acao.Should().Throw<InvalidOperationException>().WithMessage("*PrefixoAmbiente*");
    }
}
