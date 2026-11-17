namespace Oficina.Billing.Infraestrutura.MercadoPago;

public class OpcoesMercadoPago
{
    public const string Secao = "MercadoPago";

    public string UrlBase { get; set; } = "https://api.mercadopago.com";

    public string AccessToken { get; set; } = "";

    public string WebhookSecret { get; set; } = "";

    public string? UrlNotificacao { get; set; }
}
