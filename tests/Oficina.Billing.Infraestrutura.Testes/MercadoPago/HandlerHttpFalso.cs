using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Oficina.Billing.Infraestrutura.Testes.MercadoPago;

public sealed record RequisicaoGravada(HttpMethod Metodo, Uri Uri, HttpRequestHeaders Cabecalhos, string? Corpo);

/// <summary>Grava as requisicoes e devolve a resposta programada (ou lanca a excecao programada).</summary>
public sealed class HandlerHttpFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<RequisicaoGravada> Requisicoes { get; } = [];

    public static HandlerHttpFalso Json(HttpStatusCode status, string json) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var corpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requisicoes.Add(new RequisicaoGravada(request.Method, request.RequestUri!, request.Headers, corpo));
        return responder(request);
    }
}
