using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Microsoft.Extensions.Options;
using Oficina.Billing.Infraestrutura.Persistencia;
using Testcontainers.DynamoDb;

namespace Oficina.Billing.Infraestrutura.Testes.Persistencia;

/// <summary>Um DynamoDB Local por classe de teste; cada teste cria a propria tabela.</summary>
public sealed class DynamoFixture : IAsyncLifetime
{
    private readonly DynamoDbContainer _container = new DynamoDbBuilder("amazon/dynamodb-local:2.5.4")
        .Build();

    public IAmazonDynamoDB Cliente { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Cliente = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = _container.GetConnectionString(), AuthenticationRegion = "us-east-1" });
    }

    /// <summary>Cria uma tabela exclusiva pelo <see cref="InicializadorTabelaDynamo"/> e devolve as opcoes apontando para ela.</summary>
    public async Task<IOptions<OpcoesDynamo>> CriarTabelaAsync()
    {
        var opcoes = Options.Create(new OpcoesDynamo
        {
            NomeTabela = $"oficina-billing-teste-{Guid.NewGuid():N}",
            CriarTabelaSeAusente = true,
        });
        await new InicializadorTabelaDynamo(Cliente, opcoes).StartAsync(CancellationToken.None);
        return opcoes;
    }

    public async Task DisposeAsync()
    {
        Cliente?.Dispose();
        await _container.DisposeAsync();
    }
}
