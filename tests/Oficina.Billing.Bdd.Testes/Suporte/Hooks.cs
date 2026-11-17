using Reqnroll;
using Testcontainers.DynamoDb;

namespace Oficina.Billing.Bdd.Testes.Suporte;

[Binding]
public sealed class Hooks(AmbienteBdd ambiente)
{
    private static DynamoDbContainer? _container;

    [BeforeTestRun]
    public static async Task IniciarContainer()
    {
        _container = new DynamoDbBuilder("amazon/dynamodb-local:2.5.2").Build();
        await _container.StartAsync();
        AmbienteBdd.UrlDynamo = _container.GetConnectionString();
    }

    [AfterTestRun]
    public static async Task EncerrarContainer()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    [AfterScenario]
    public async Task EncerrarFabrica() => await ambiente.DisposeAsync();
}
