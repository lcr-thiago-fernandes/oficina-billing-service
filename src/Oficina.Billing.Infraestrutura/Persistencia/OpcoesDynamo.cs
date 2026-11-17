namespace Oficina.Billing.Infraestrutura.Persistencia;

public class OpcoesDynamo
{
    public const string Secao = "Dynamo";

    public string NomeTabela { get; set; } = "oficina-billing-local";

    /// <summary>Endpoint alternativo (DynamoDB Local). Nulo = endpoint padrao da regiao.</summary>
    public string? ServiceUrl { get; set; }

    public string Regiao { get; set; } = "us-east-1";

    /// <summary>true so em ambiente local/testes: cria a tabela na subida se ela nao existir.</summary>
    public bool CriarTabelaSeAusente { get; set; }
}
