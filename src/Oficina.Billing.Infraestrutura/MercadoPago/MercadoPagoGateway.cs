using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Oficina.Billing.Aplicacao.Portas;

namespace Oficina.Billing.Infraestrutura.MercadoPago;

/// <summary>
/// Gateway real: Checkout Pro (preferencias), consulta de pagamento e estorno total.
/// 5xx/timeout/rede viram <see cref="GatewayPagamentoIndisponivelException"/> (o retry da saga repete);
/// 4xx viram <see cref="InvalidOperationException"/> (nao adianta repetir). O token nunca entra em mensagens.
/// </summary>
public class MercadoPagoGateway(HttpClient http, IOptions<OpcoesMercadoPago> opcoes) : IGatewayPagamento
{
    private const int LimiteCorpoNaMensagem = 500;
    private readonly OpcoesMercadoPago _opcoes = opcoes.Value;

    public string Nome => "MercadoPago";

    public async Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken ct)
    {
        var corpo = new PreferenciaRequisicao(
            cobranca.Itens.Select(i => new ItemPreferencia(i.Titulo, i.Quantidade, i.PrecoUnitario, "BRL")).ToList(),
            cobranca.OrdemServicoId.ToString(),
            _opcoes.UrlNotificacao,
            string.IsNullOrWhiteSpace(cobranca.EmailPagador) ? null : new PagadorPreferencia(cobranca.EmailPagador));

        using var req = Requisicao(HttpMethod.Post, "/checkout/preferences", $"preferencia-{cobranca.OrdemServicoId}");
        req.Content = JsonContent.Create(corpo);
        using var resp = await EnviarAsync(req, ct);
        await GarantirSucessoAsync(resp, ct);

        var r = await resp.Content.ReadFromJsonAsync<PreferenciaResposta>(ct);
        if (r?.Id is null || r.InitPoint is null)
            throw new GatewayPagamentoIndisponivelException("Resposta invalida do Mercado Pago ao criar a preferencia");
        return new CobrancaCriada(r.Id, r.InitPoint);
    }

    public async Task<PagamentoGateway?> ObterPagamentoAsync(string pagamentoId, CancellationToken ct)
    {
        using var req = Requisicao(HttpMethod.Get, $"/v1/payments/{Uri.EscapeDataString(pagamentoId)}", null);
        using var resp = await EnviarAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        await GarantirSucessoAsync(resp, ct);

        var r = await resp.Content.ReadFromJsonAsync<PagamentoResposta>(ct)
            ?? throw new GatewayPagamentoIndisponivelException("Resposta vazia do Mercado Pago ao consultar o pagamento");
        Guid? osId = Guid.TryParse(r.ExternalReference, out var g) ? g : null;
        return new PagamentoGateway(pagamentoId, osId, MapearStatus(r.Status), r.DetalheStatus, r.Valor, r.Meio ?? "");
    }

    public async Task<EstornoGateway> EstornarAsync(string pagamentoId, decimal valor, string chaveIdempotencia,
        CancellationToken ct)
    {
        using var req = Requisicao(HttpMethod.Post, $"/v1/payments/{Uri.EscapeDataString(pagamentoId)}/refunds",
            chaveIdempotencia);
        req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"); // estorno total
        using var resp = await EnviarAsync(req, ct);
        await GarantirSucessoAsync(resp, ct);

        var r = await resp.Content.ReadFromJsonAsync<EstornoResposta>(ct)
            ?? throw new GatewayPagamentoIndisponivelException("Resposta vazia do Mercado Pago ao estornar");
        return new EstornoGateway(r.Id.ToString(CultureInfo.InvariantCulture), r.Valor);
    }

    private static StatusPagamentoGateway MapearStatus(string? status) => status switch
    {
        "approved" => StatusPagamentoGateway.Aprovado,
        "rejected" or "cancelled" => StatusPagamentoGateway.Recusado,
        "refunded" or "charged_back" => StatusPagamentoGateway.Estornado,
        _ => StatusPagamentoGateway.Pendente, // pending, in_process, authorized, in_mediation e desconhecidos
    };

    private HttpRequestMessage Requisicao(HttpMethod metodo, string caminho, string? chaveIdempotencia)
    {
        var req = new HttpRequestMessage(metodo, _opcoes.UrlBase.TrimEnd('/') + caminho);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opcoes.AccessToken);
        if (chaveIdempotencia is not null)
            req.Headers.Add("X-Idempotency-Key", chaveIdempotencia);
        return req;
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new GatewayPagamentoIndisponivelException("Falha de comunicacao com o Mercado Pago", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout do HttpClient (nao foi o chamador que cancelou).
            throw new GatewayPagamentoIndisponivelException("Timeout na chamada ao Mercado Pago", ex);
        }
    }

    private static async Task GarantirSucessoAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode)
            return;

        var corpo = await resp.Content.ReadAsStringAsync(ct);
        if (corpo.Length > LimiteCorpoNaMensagem) corpo = corpo[..LimiteCorpoNaMensagem];
        var status = (int)resp.StatusCode;
        if (status >= 500 || status == 429 || status == 408)
            throw new GatewayPagamentoIndisponivelException($"Mercado Pago indisponivel (HTTP {status}): {corpo}");
        throw new InvalidOperationException($"Mercado Pago recusou a requisicao (HTTP {status}): {corpo}");
    }
}
