using System.Net.Http.Headers;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Oficina.Billing.Api.Testes;
using Oficina.Billing.Infraestrutura.Mensageria;

namespace Oficina.Billing.Bdd.Testes.Suporte;

/// <summary>
/// Contexto do cenario: uma API real por cenario (DynamoDB Local unico da execucao, tabela propria, harness do
/// MassTransit com o mesmo registro de producao via <see cref="ConfiguracaoMensageria.ConfigurarRegistro"/>).
/// </summary>
public sealed class AmbienteBdd : IAsyncDisposable
{
    public const string Prefixo = "test";

    public static string? UrlDynamo { get; set; }

    private FabricaBdd? _fabrica;

    public FabricaBdd Fabrica => _fabrica ??= new FabricaBdd(
        UrlDynamo ?? throw new InvalidOperationException("Container DynamoDB nao iniciado."));

    /// <summary>Um OrdemServicoId novo por cenario (o numero da OS e so o numero de negocio).</summary>
    public Guid OrdemServicoId { get; } = Guid.NewGuid();

    public async ValueTask DisposeAsync()
    {
        if (_fabrica is not null) await _fabrica.DisposeAsync();
        _fabrica = null;
    }
}

public sealed class FabricaBdd(string urlDynamo) : WebApplicationFactory<Program>
{
    private readonly string _tabela = $"oficina-billing-bdd-{Guid.NewGuid():N}";

    public ITestHarness Harness => Services.GetTestHarness();

    public string FilaComandos => TopologiaSaga.NomeFila(AmbienteBdd.Prefixo);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var configuracoes = new Dictionary<string, string>
        {
            ["Jwt:Secret"] = GeradorTokenTeste.Segredo,
            ["Mensageria:Transporte"] = "InMemory",
            ["Mensageria:PrefixoAmbiente"] = AmbienteBdd.Prefixo,
            ["Pagamento:Modo"] = "Simulado",
            ["Dynamo:ServiceUrl"] = urlDynamo,
            ["Dynamo:NomeTabela"] = _tabela,
            ["Dynamo:CriarTabelaSeAusente"] = "true",
        };
        foreach (var (chave, valor) in configuracoes)
            builder.UseSetting(chave, valor);

        builder.ConfigureTestServices(s => s.AddMassTransitTestHarness(x => ConfiguracaoMensageria.ConfigurarRegistro(x,
            new OpcoesMensageria { Transporte = "InMemory", PrefixoAmbiente = AmbienteBdd.Prefixo })));
    }

    public HttpClient Cliente(string perfil, string? documento = null)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GeradorTokenTeste.Gerar(perfil, documento));
        return cliente;
    }
}
