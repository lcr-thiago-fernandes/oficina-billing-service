using System.Text.RegularExpressions;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Oficina.Billing.Infraestrutura.Mensageria.Consumidores;

namespace Oficina.Billing.Infraestrutura.Mensageria;

public static partial class ConfiguracaoMensageria
{
    public const string TransporteAmazonSqs = "AmazonSqs";
    public const string TransporteInMemory = "InMemory";

    public static IServiceCollection AdicionarMensageria(this IServiceCollection servicos, OpcoesMensageria opcoes) =>
        servicos.AddMassTransit(x => ConfigurarRegistro(x, opcoes));

    /// <summary>
    /// Toda a configuracao do bus, compartilhada entre producao (<c>AddMassTransit</c>) e testes
    /// (<c>AddMassTransitTestHarness</c>, que no 8.5.12 substitui o registro existente).
    /// </summary>
    public static void ConfigurarRegistro(IBusRegistrationConfigurator x, OpcoesMensageria opcoes)
    {
        var prefixo = opcoes.PrefixoAmbiente;
        if (string.IsNullOrEmpty(prefixo) || !PrefixoValido().IsMatch(prefixo))
            throw new InvalidOperationException(
                $"Valor de {OpcoesMensageria.Secao}:PrefixoAmbiente invalido: '{prefixo}'. " +
                "Use apenas letras minusculas, digitos e '-' (ex.: local, hml, prd).");

        x.AddConsumer<GerarOrcamentoConsumidor>();
        x.AddConsumer<CancelarOrcamentoConsumidor>();
        x.AddConsumer<EstornarPagamentoConsumidor>();

        if (string.Equals(opcoes.Transporte, TransporteAmazonSqs, StringComparison.OrdinalIgnoreCase))
        {
            x.UsingAmazonSqs((ctx, cfg) =>
            {
                cfg.Host(opcoes.Regiao, h =>
                {
                    if (!string.IsNullOrWhiteSpace(opcoes.ServiceUrl))
                    {
                        // Somente LocalStack: endpoint local e chaves ficticias. Na AWS vale a cadeia padrao (IRSA).
                        h.Config(new AmazonSQSConfig { ServiceURL = opcoes.ServiceUrl });
                        h.Config(new AmazonSimpleNotificationServiceConfig { ServiceURL = opcoes.ServiceUrl });
                        h.AccessKey("test");
                        h.SecretKey("test");
                    }
                });
                // O nome padrao {maquina}_{assembly}_bus_{id} daria AccessDenied no EKS (IAM so permite ${amb}-oficina-*).
                // A fila e auto-delete e cada replica precisa da sua: sufixo com a instancia (nome do pod).
                cfg.OverrideDefaultBusEndpointQueueName(TopologiaSaga.NomeFilaBus(prefixo, TopologiaSaga.InstanciaAtual()));
                ConfigurarBus(ctx, cfg, opcoes, redeliveryAtrasado: true);
            });
        }
        else if (string.Equals(opcoes.Transporte, TransporteInMemory, StringComparison.OrdinalIgnoreCase))
        {
            // O InMemory do 8.5.12 nao expoe OverrideDefaultBusEndpointQueueName (so SQS/SQL); sem IAM, o nome e irrelevante.
            x.UsingInMemory((ctx, cfg) => ConfigurarBus(ctx, cfg, opcoes, redeliveryAtrasado: false));
        }
        else
        {
            throw new InvalidOperationException(
                $"Valor de {OpcoesMensageria.Secao}:Transporte desconhecido: '{opcoes.Transporte}'. " +
                $"Valores aceitos: {TransporteAmazonSqs}, {TransporteInMemory}.");
        }
    }

    private static void ConfigurarBus(IBusRegistrationContext ctx, IBusFactoryConfigurator cfg, OpcoesMensageria opcoes,
        bool redeliveryAtrasado)
    {
        var prefixo = opcoes.PrefixoAmbiente;
        TopologiaSaga.Aplicar(cfg, prefixo);

        cfg.ReceiveEndpoint(TopologiaSaga.NomeFila(prefixo), e =>
        {
            // Comandos chegam por send direto na fila: sem assinatura de topico.
            e.ConfigureConsumeTopology = false;
            // Fault<T> viraria topico nomeado pelo tipo .NET (fora do IAM *-oficina-*); falha definitiva vai para a _error.
            // No 8.5.12 PublishFaults so existe no receive endpoint (nao no configurador do bus).
            e.PublishFaults = false;
            if (redeliveryAtrasado)
                e.UseDelayedRedelivery(r => r.Intervals(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15)));
            var minimo = opcoes.IntervaloMinimoRetry;
            e.UseMessageRetry(r => r.Exponential(EstornarPagamentoConsumidor.LimiteRetry, minimo, minimo * 30, minimo * 5));
            e.UseConsumeFilter(typeof(FiltroLogOrdemServico<>), ctx);
            e.ConfigureConsumer<GerarOrcamentoConsumidor>(ctx);
            e.ConfigureConsumer<CancelarOrcamentoConsumidor>(ctx);
            e.ConfigureConsumer<EstornarPagamentoConsumidor>(ctx);
        });
    }

    [GeneratedRegex(@"^[a-z0-9-]+\z")]
    private static partial Regex PrefixoValido();
}
