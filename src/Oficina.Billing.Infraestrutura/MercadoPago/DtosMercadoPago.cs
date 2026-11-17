using System.Text.Json.Serialization;

namespace Oficina.Billing.Infraestrutura.MercadoPago;

internal record PreferenciaRequisicao(
    [property: JsonPropertyName("items")] IReadOnlyList<ItemPreferencia> Itens,
    [property: JsonPropertyName("external_reference")] string ExternalReference,
    [property: JsonPropertyName("notification_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UrlNotificacao,
    [property: JsonPropertyName("payer"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PagadorPreferencia? Pagador);

internal record ItemPreferencia(
    [property: JsonPropertyName("title")] string Titulo,
    [property: JsonPropertyName("quantity")] int Quantidade,
    [property: JsonPropertyName("unit_price")] decimal PrecoUnitario,
    [property: JsonPropertyName("currency_id")] string Moeda);

internal record PagadorPreferencia([property: JsonPropertyName("email")] string Email);

internal record PreferenciaResposta(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("init_point")] string? InitPoint);

internal record PagamentoResposta(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("status_detail")] string? DetalheStatus,
    [property: JsonPropertyName("external_reference")] string? ExternalReference,
    [property: JsonPropertyName("transaction_amount")] decimal Valor,
    [property: JsonPropertyName("payment_method_id")] string? Meio);

internal record EstornoResposta(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("amount")] decimal Valor);
