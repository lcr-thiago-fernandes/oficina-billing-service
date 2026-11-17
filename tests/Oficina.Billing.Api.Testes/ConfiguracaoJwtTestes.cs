namespace Oficina.Billing.Api.Testes;

/// <summary>Configuracao invalida derruba a inicializacao (falha alta), em vez de subir uma API insegura.</summary>
public class ConfiguracaoJwtTestes
{
    private static Exception FalhaAoIniciar(FabricaApi fabrica)
    {
        var acao = () => fabrica.CreateClient();
        return acao.Should().Throw<Exception>().Which;
    }

    private static string Cadeia(Exception? excecao)
    {
        var mensagens = new List<string>();
        for (var atual = excecao; atual is not null; atual = atual.InnerException)
            mensagens.Add(atual.Message);
        return string.Join(" | ", mensagens);
    }

    [Fact]
    public void SegredoCurto_LancaNaInicializacao()
    {
        using var fabrica = new FabricaApi(configuracoes: new Dictionary<string, string?> { ["Jwt:Secret"] = "curto" });
        Cadeia(FalhaAoIniciar(fabrica)).Should().Contain("Jwt:Secret");
    }

    [Fact]
    public void ModoMercadoPagoSemAccessToken_LancaNaInicializacao()
    {
        using var fabrica = new FabricaApi("MercadoPago");
        Cadeia(FalhaAoIniciar(fabrica)).Should().Contain("MercadoPago:AccessToken");
    }

    [Fact]
    public void ModoMercadoPagoSemWebhookSecret_LancaNaInicializacao()
    {
        using var fabrica = new FabricaApi("MercadoPago", new Dictionary<string, string?>
        {
            ["MercadoPago:AccessToken"] = "TEST-token",
            ["MercadoPago:WebhookSecret"] = "",
        });
        Cadeia(FalhaAoIniciar(fabrica)).Should().Contain("MercadoPago:WebhookSecret");
    }
}
