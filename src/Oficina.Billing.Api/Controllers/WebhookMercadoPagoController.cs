using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Infraestrutura.MercadoPago;

namespace Oficina.Billing.Api.Controllers;

/// <summary>
/// Notificacoes do Mercado Pago. Publico (sem JWT): a autenticidade vem do header <c>x-signature</c>.
/// </summary>
[ApiController]
[Route("webhooks/mercadopago")]
[AllowAnonymous]
public class WebhookMercadoPagoController(
    ProcessarNotificacaoPagamentoCasoDeUso processar,
    IOptions<OpcoesMercadoPago> opcoes,
    ILogger<WebhookMercadoPagoController> logger) : ControllerBase
{
    private const string TipoPagamento = "payment";

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Receber(CancellationToken ct)
    {
        var assinatura = Request.Headers["x-signature"].ToString();
        var requestId = Request.Headers["x-request-id"].ToString();
        var dataId = Request.Query["data.id"].ToString();
        var tipo = Request.Query["type"].ToString();
        if (string.IsNullOrEmpty(tipo))
            tipo = Request.Query["topic"].ToString();

        if (!ValidadorAssinaturaMercadoPago.Validar(assinatura, requestId, dataId, opcoes.Value.WebhookSecret))
        {
            logger.LogWarning("Webhook do Mercado Pago com assinatura invalida (data.id {DataId})", dataId);
            return Unauthorized();
        }

        if (!string.Equals(tipo, TipoPagamento, StringComparison.Ordinal))
            return Ok();

        if (string.IsNullOrEmpty(dataId))
            return BadRequest();

        try
        {
            var resultado = await processar.ExecutarAsync(dataId, ct);
            logger.LogInformation("Webhook do pagamento {PagamentoId} processado: {Resultado}", dataId, resultado);
            return Ok();
        }
        // F8: o middleware responderia 502/409; para o Mercado Pago a falha transitoria e sempre 500, e ele reenvia
        // (o processamento e idempotente).
        catch (Exception ex) when (ex is GatewayPagamentoIndisponivelException or ConcorrenciaException)
        {
            logger.LogError(ex, "Falha transitoria ao processar o webhook do pagamento {PagamentoId}", dataId);
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Falha transitoria ao processar a notificacao", detail: "O Mercado Pago deve reenviar.");
        }
    }
}
