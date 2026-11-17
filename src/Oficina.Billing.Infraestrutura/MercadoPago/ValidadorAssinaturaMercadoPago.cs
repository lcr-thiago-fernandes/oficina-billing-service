using System.Security.Cryptography;
using System.Text;

namespace Oficina.Billing.Infraestrutura.MercadoPago;

/// <summary>
/// Valida o header <c>x-signature</c> (<c>ts=&lt;ts&gt;,v1=&lt;hex&gt;</c>) das notificacoes do Mercado Pago.
/// Manifesto: <c>id:&lt;data.id&gt;;request-id:&lt;x-request-id&gt;;ts:&lt;ts&gt;;</c> (pares ausentes sao removidos).
/// Sem janela de tempo sobre <c>ts</c>: a unidade nao e garantida pela doc; replay e neutralizado pelo dedupe por payment_id.
/// </summary>
public static class ValidadorAssinaturaMercadoPago
{
    public static bool Validar(string? xSignature, string? xRequestId, string? dataId, string segredo)
    {
        if (string.IsNullOrWhiteSpace(xSignature) || string.IsNullOrEmpty(segredo))
            return false;

        string? ts = null, v1 = null;
        foreach (var parte in xSignature.Split(','))
        {
            var i = parte.IndexOf('=');
            if (i <= 0) continue;
            var chave = parte[..i].Trim();
            var valor = parte[(i + 1)..].Trim();
            if (chave == "ts") ts = valor;
            else if (chave == "v1") v1 = valor;
        }

        if (string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(v1))
            return false;

        byte[] esperado;
        try
        {
            esperado = Convert.FromHexString(v1);
        }
        catch (FormatException)
        {
            return false;
        }

        var manifesto = new StringBuilder();
        if (!string.IsNullOrEmpty(dataId)) manifesto.Append("id:").Append(dataId.ToLowerInvariant()).Append(';');
        if (!string.IsNullOrEmpty(xRequestId)) manifesto.Append("request-id:").Append(xRequestId).Append(';');
        manifesto.Append("ts:").Append(ts).Append(';');

        var calculado = HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(manifesto.ToString()));
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }
}
