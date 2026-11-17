namespace Oficina.Billing.Infraestrutura.Mensageria;

public class OpcoesMensageria
{
    public const string Secao = "Mensageria";

    /// <summary><c>AmazonSqs</c> ou <c>InMemory</c>.</summary>
    public string Transporte { get; set; } = ConfiguracaoMensageria.TransporteInMemory;

    /// <summary>Prefixo de ambiente de filas e topicos (<c>local</c>, <c>hml</c>, <c>prd</c>).</summary>
    public string PrefixoAmbiente { get; set; } = "local";

    public string Regiao { get; set; } = "us-east-1";

    /// <summary>Endpoint alternativo (LocalStack). Nulo = AWS com a cadeia padrao de credenciais (IRSA).</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Primeiro intervalo do retry exponencial da fila de comandos; maximo = 30x e incremento = 5x este valor
    /// (padrao 1 s / 30 s / 5 s). Reduzido apenas nos testes.
    /// </summary>
    public TimeSpan IntervaloMinimoRetry { get; set; } = TimeSpan.FromSeconds(1);
}
