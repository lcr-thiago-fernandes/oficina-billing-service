using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Infraestrutura.Mensageria;
using Oficina.Billing.Infraestrutura.MercadoPago;
using Oficina.Billing.Infraestrutura.Persistencia;
using Oficina.Billing.Infraestrutura.Simulador;
using Oficina.Billing.Infraestrutura.Telemetria;

namespace Oficina.Billing.Infraestrutura;

public static class InjecaoDeDependencia
{
    private static readonly TimeSpan TimeoutMercadoPago = TimeSpan.FromSeconds(10);

    public static IServiceCollection AdicionarInfraestrutura(this IServiceCollection servicos, IConfiguration configuracao)
    {
        servicos.TryAddSingleton(TimeProvider.System);

        AdicionarDynamo(servicos, configuracao);
        AdicionarGateway(servicos, configuracao);

        servicos.AddSingleton<ITelemetriaPagamento, TelemetriaPagamentoNewRelic>();
        servicos.AddScoped<IPublicadorMensagens, PublicadorMassTransit>();

        var mensageria = configuracao.GetSection(OpcoesMensageria.Secao).Get<OpcoesMensageria>() ?? new OpcoesMensageria();
        servicos.AdicionarMensageria(mensageria);
        return servicos;
    }

    private static void AdicionarDynamo(IServiceCollection servicos, IConfiguration configuracao)
    {
        var secao = configuracao.GetSection(OpcoesDynamo.Secao);
        servicos.Configure<OpcoesDynamo>(secao);
        var dynamo = secao.Get<OpcoesDynamo>() ?? new OpcoesDynamo();

        servicos.AddSingleton<IAmazonDynamoDB>(_ => string.IsNullOrWhiteSpace(dynamo.ServiceUrl)
            // AWS: cadeia padrao de credenciais (IRSA no EKS, via AWSSDK.SecurityToken).
            ? new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName(dynamo.Regiao))
            // DynamoDB Local: chaves ficticias, sem depender de AWS_* no ambiente.
            : new AmazonDynamoDBClient(new BasicAWSCredentials("test", "test"),
                new AmazonDynamoDBConfig { ServiceURL = dynamo.ServiceUrl, AuthenticationRegion = dynamo.Regiao }));
        servicos.AddSingleton<IRepositorioBilling, RepositorioBillingDynamo>();

        if (dynamo.CriarTabelaSeAusente)
            servicos.AddHostedService<InicializadorTabelaDynamo>();
    }

    private static void AdicionarGateway(IServiceCollection servicos, IConfiguration configuracao)
    {
        var secao = configuracao.GetSection(OpcoesPagamento.Secao);
        servicos.Configure<OpcoesPagamento>(secao);
        servicos.Configure<OpcoesMercadoPago>(configuracao.GetSection(OpcoesMercadoPago.Secao));
        var pagamento = secao.Get<OpcoesPagamento>() ?? new OpcoesPagamento();

        if (pagamento.Modo == ModoPagamento.MercadoPago)
        {
            servicos.AddHttpClient<IGatewayPagamento, MercadoPagoGateway>(c => c.Timeout = TimeoutMercadoPago);
        }
        else
        {
            // Singleton unico: o endpoint do simulador registra pagamentos que o processamento consulta depois.
            servicos.AddSingleton<GatewayPagamentoSimulado>();
            servicos.AddSingleton<IGatewayPagamento>(sp => sp.GetRequiredService<GatewayPagamentoSimulado>());
        }
    }
}
