using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Oficina.Billing.Api.Testes;

/// <summary>
/// Assina tokens localmente nos testes. Em producao quem emite e a Lambda oficina-auth; o servico apenas valida
/// (iss=oficina-auth, aud=oficina-api, HS256, claims "perfil" e "documento").
/// </summary>
public static class GeradorTokenTeste
{
    public const string Segredo = "chave-de-teste-com-pelo-menos-32-caracteres!!";
    public const string Emissor = "oficina-auth";
    public const string Audiencia = "oficina-api";

    public static string Gerar(string perfil, string? documento = null, string audiencia = Audiencia,
        string emissor = Emissor, string segredo = Segredo, bool expirado = false)
    {
        var credenciais = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(segredo)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("perfil", perfil),
            new("jti", Guid.NewGuid().ToString()),
        };
        if (documento is not null)
            claims.Add(new Claim("documento", documento));

        var token = new JwtSecurityToken(
            issuer: emissor,
            audience: audiencia,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(expirado ? -120 : -1),
            expires: DateTime.UtcNow.AddMinutes(expirado ? -60 : 60),
            signingCredentials: credenciais);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
