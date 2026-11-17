using System.Security.Claims;

namespace Oficina.Billing.Api.Configuracao;

public static class PoliticasDeAutorizacao
{
    public const string RequerAdmin = "RequerAdmin";
    public const string RequerClienteOuEquipe = "RequerClienteOuEquipe";

    public const string PerfilCliente = "Cliente";
    public const string PerfilAtendente = "Atendente";
    public const string PerfilAdmin = "Admin";

    public static IServiceCollection AdicionarPoliticas(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(RequerAdmin, p =>
                p.RequireAuthenticatedUser().RequireClaim(ExtensoesClaims.ClaimPerfil, PerfilAdmin));

            // Equipe (Atendente/Admin) acessa qualquer OS; Cliente so com a claim documento (sem ela: 403, como no
            // oficina-app). A propriedade da OS e verificada no caso de uso (nao-dono: 404).
            options.AddPolicy(RequerClienteOuEquipe, p =>
                p.RequireAuthenticatedUser().RequireAssertion(ctx => ClienteOuEquipe(ctx.User)));
        });
        return services;
    }

    private static bool ClienteOuEquipe(ClaimsPrincipal usuario) => usuario.PerfilDoUsuario() switch
    {
        PerfilAtendente or PerfilAdmin => true,
        PerfilCliente => usuario.DocumentoDoCliente() is not null,
        _ => false,
    };
}
