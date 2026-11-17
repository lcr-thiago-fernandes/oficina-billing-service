using System.Text.RegularExpressions;

namespace Oficina.Billing.Dominio.Comum;

public static class DocumentoBr
{
    private static readonly Regex NaoDigitos = new(@"\D", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string Normalizar(string? valor) => NaoDigitos.Replace(valor ?? "", "");
}
