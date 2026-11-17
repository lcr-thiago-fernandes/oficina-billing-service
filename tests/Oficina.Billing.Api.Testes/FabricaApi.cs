using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Infraestrutura.Mensageria;
using Oficina.Billing.Infraestrutura.Simulador;

namespace Oficina.Billing.Api.Testes;

/// <summary>
/// Sobe a API real com o repositorio em memoria e o harness do MassTransit no lugar do bus (mesmo registro de
/// producao via <see cref="ConfiguracaoMensageria.ConfigurarRegistro"/>, F3).
/// </summary>
public sealed class FabricaApi : WebApplicationFactory<Program>
{
    public const string SegredoWebhook = "segredo-teste";
    public const string Prefixo = "local";

    private readonly Dictionary<string, string?> _configuracoes;
    private readonly Action<IServiceCollection>? _servicos;

    /// <param name="configuracoes">Sobrescreve/acrescenta chaves (formato <c>Secao:Chave</c>); valor nulo remove.</param>
    public FabricaApi(string modo = "Simulado", IDictionary<string, string?>? configuracoes = null,
        Action<IServiceCollection>? servicos = null)
    {
        _configuracoes = new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = GeradorTokenTeste.Segredo,
            ["Mensageria:Transporte"] = "InMemory",
            ["Mensageria:PrefixoAmbiente"] = Prefixo,
            ["Pagamento:Modo"] = modo,
            ["MercadoPago:WebhookSecret"] = SegredoWebhook,
        };
        foreach (var (chave, valor) in configuracoes ?? new Dictionary<string, string?>())
            _configuracoes[chave] = valor;
        _servicos = servicos;
    }

    public RepositorioBillingEmMemoria Repositorio { get; } = new();

    public ITestHarness Harness => Services.GetTestHarness();

    public GatewayPagamentoSimulado GatewaySimulado => Services.GetRequiredService<GatewayPagamentoSimulado>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (chave, valor) in _configuracoes)
            builder.UseSetting(chave, valor);

        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IRepositorioBilling>(Repositorio);
            // O harness substitui o bus registrado pelo Program; consumidores e endpoints seguem os de producao.
            s.AddMassTransitTestHarness(x => ConfiguracaoMensageria.ConfigurarRegistro(x,
                new OpcoesMensageria { Transporte = "InMemory", PrefixoAmbiente = Prefixo }));
            _servicos?.Invoke(s);
        });
    }

    public HttpClient Cliente(string perfil, string? documento = null)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GeradorTokenTeste.Gerar(perfil, documento));
        return cliente;
    }

    public HttpClient ClienteComToken(string token)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    /// <summary>Monta o header <c>x-signature</c> como o Mercado Pago (manifesto <c>id;request-id;ts</c>).</summary>
    public static string AssinarWebhook(string dataId, string requestId, string ts = "1742505638683",
        string segredo = SegredoWebhook)
    {
        var manifesto = $"id:{dataId.ToLowerInvariant()};request-id:{requestId};ts:{ts};";
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(manifesto));
        return $"ts={ts},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}

internal static class Cenario
{
    public const string Cpf = "52998224725";
    public const string CpfFormatado = "529.982.247-25";
    public const string OutroCpf = "11144477735";
    public static readonly Guid OsId = Guid.Parse("3f2b8c1e-6a4d-4e7b-9c3a-1d2e3f4a5b6c");

    public static string RotaOrcamento => $"/billing/api/v1/orcamentos/{OsId}";
    public static string RotaPagamentos => $"/billing/api/v1/pagamentos/{OsId}";
    public static string RotaSimulador => $"/billing/api/v1/simulador/pagamentos/{OsId}";

    /// <summary>Orcamento de 191,00 (91,00 + 2 x 50,00) do cliente <see cref="Cpf"/>.</summary>
    public static Orcamento Orcamento(StatusOrcamento status = StatusOrcamento.Gerado)
    {
        var agora = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var o = Dominio.Orcamentos.Orcamento.Gerar(OsId, 42, new DadosCliente(Cpf, "Maria", "maria@exemplo.com"),
            new[]
            {
                new ItemOrcamento(TipoItem.Servico, Guid.NewGuid(), "Troca de oleo", 1, 91.00m),
                new ItemOrcamento(TipoItem.Peca, Guid.NewGuid(), "Filtro", 2, 50.00m),
            }, agora);
        var aprovado = status == StatusOrcamento.Aprovado;
        return Dominio.Orcamentos.Orcamento.Reconstituir(o.Id, o.OrdemServicoId, o.NumeroOs, o.Cliente, o.Itens,
            o.ValorTotal, status, null, aprovado ? "sim-pref" : null,
            aprovado ? "https://simulador.local/pagamentos/x" : null, o.CriadoEm, o.AtualizadoEm, 0);
    }
}
