using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Oficina.Billing.Infraestrutura.Persistencia;

/// <summary>
/// Cria a tabela na subida quando <see cref="OpcoesDynamo.CriarTabelaSeAusente"/> (ambiente local e testes; em
/// hml/prd a tabela vem do Terraform) e espera ela ficar <c>ACTIVE</c>.
/// </summary>
public class InicializadorTabelaDynamo(IAmazonDynamoDB cliente, IOptions<OpcoesDynamo> opcoes) : IHostedService
{
    private static readonly TimeSpan IntervaloEspera = TimeSpan.FromMilliseconds(200);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var o = opcoes.Value;
        if (!o.CriarTabelaSeAusente)
            return;

        try
        {
            await cliente.CreateTableAsync(new CreateTableRequest
            {
                TableName = o.NomeTabela,
                BillingMode = BillingMode.PAY_PER_REQUEST,
                AttributeDefinitions =
                [
                    new AttributeDefinition("pk", ScalarAttributeType.S),
                    new AttributeDefinition("sk", ScalarAttributeType.S),
                ],
                KeySchema =
                [
                    new KeySchemaElement("pk", KeyType.HASH),
                    new KeySchemaElement("sk", KeyType.RANGE),
                ],
            }, cancellationToken);
        }
        catch (ResourceInUseException)
        {
            // Ja existe (outra replica criou ou subida repetida).
        }

        while ((await cliente.DescribeTableAsync(o.NomeTabela, cancellationToken)).Table.TableStatus !=
               TableStatus.ACTIVE)
        {
            await Task.Delay(IntervaloEspera, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
