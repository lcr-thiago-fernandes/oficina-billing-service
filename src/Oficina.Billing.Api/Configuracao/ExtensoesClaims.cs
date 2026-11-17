using System.Security.Claims;
using Oficina.Billing.Aplicacao.CasosDeUso;

namespace Oficina.Billing.Api.Configuracao;

/// <summary>
/// Leitura tipada das claims emitidas pela Lambda oficina-auth. A propriedade da OS e decidida por
/// <c>documento</c> (CPF), nunca por <c>sub</c>: <c>sub</c> nao tem significado uniforme entre perfis.
/// </summary>
public static class ExtensoesClaims
{
    public const string ClaimDocumento = "documento";
    public const string ClaimPerfil = "perfil";

    /// <summary>CPF do cliente autenticado. Nulo em tokens de Admin/Atendente.</summary>
    public static string? DocumentoDoCliente(this ClaimsPrincipal usuario)
    {
        var valor = usuario.FindFirst(ClaimDocumento)?.Value;
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    public static string? PerfilDoUsuario(this ClaimsPrincipal usuario) => usuario.FindFirst(ClaimPerfil)?.Value;

    public static Solicitante Solicitante(this ClaimsPrincipal usuario) =>
        new(usuario.PerfilDoUsuario() ?? string.Empty, usuario.DocumentoDoCliente());
}
