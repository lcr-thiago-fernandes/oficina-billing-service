using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MassTransit;
using Oficina.Billing.Aplicacao.Mensagens;

namespace Oficina.Billing.Infraestrutura.Mensageria;

/// <summary>Nomes de filas e topicos (spec §5), todos dentro do padrao IAM <c>${amb}-oficina-*</c>.</summary>
public static partial class TopologiaSaga
{
    public const int TamanhoMaximoFilaSqs = 80;

    private static readonly MethodInfo PrefixarGenerico =
        typeof(TopologiaSaga).GetMethod(nameof(Prefixar), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static string NomeFila(string prefixo) => $"{prefixo}-oficina-billing-comandos";

    /// <summary>
    /// Fila do bus (temporaria, auto-delete), uma por replica: sufixo com a instancia (nome do pod) em minusculas,
    /// caracteres fora de <c>[a-z0-9-_]</c> trocados por '-', nome inteiro truncado no limite do SQS.
    /// </summary>
    public static string NomeFilaBus(string prefixo, string instancia)
    {
        var nome = $"{prefixo}-oficina-billing-bus-{CaractereInvalido().Replace(instancia.ToLowerInvariant(), "-")}";
        return nome.Length > TamanhoMaximoFilaSqs ? nome[..TamanhoMaximoFilaSqs] : nome;
    }

    /// <summary>HOSTNAME (nome do pod no Kubernetes); sem ele, o nome da maquina.</summary>
    public static string InstanciaAtual() =>
        Environment.GetEnvironmentVariable("HOSTNAME") is { } hostname && !string.IsNullOrWhiteSpace(hostname)
            ? hostname
            : Environment.MachineName;

    public static string NomeTopico(string prefixo, Type tipo) => $"{prefixo}-oficina-saga-{ContratosSaga.Nome(tipo)}";

    /// <summary>
    /// Prefixo por tipo (o <c>[EntityName]</c> faz o 8.5.12 ignorar <c>PrefixEntityNameFormatter</c>) e enums como
    /// texto no serializador do bus.
    /// </summary>
    public static void Aplicar(IBusFactoryConfigurator cfg, string prefixo)
    {
        cfg.ConfigureJsonSerializerOptions(o =>
        {
            // As opcoes sao estaticas no 8.5.12: so adiciona uma vez, mesmo com varios bus no processo (testes).
            if (!o.Converters.OfType<JsonStringEnumConverter>().Any())
                o.Converters.Add(new JsonStringEnumConverter());
            return o;
        });

        foreach (var tipo in ContratosSaga.Tipos)
            PrefixarGenerico.MakeGenericMethod(tipo).Invoke(null, [cfg, prefixo]);
    }

    private static void Prefixar<T>(IBusFactoryConfigurator cfg, string prefixo) where T : class =>
        cfg.Message<T>(m => m.SetEntityName(NomeTopico(prefixo, typeof(T))));

    [GeneratedRegex("[^a-z0-9_-]")]
    private static partial Regex CaractereInvalido();
}
