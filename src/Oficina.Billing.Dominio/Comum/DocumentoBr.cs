using System.Text.RegularExpressions;

namespace Oficina.Billing.Dominio.Comum;

public static class DocumentoBr
{
    public static string Normalizar(string? valor) => Regex.Replace(valor ?? "", @"\D", "");
}
