using Microsoft.OpenApi.Models;

namespace Oficina.Billing.Api.Configuracao;

public static class ConfiguracaoSwagger
{
    public static IServiceCollection AdicionarSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Oficina - Billing Service",
                Version = "v1",
                Description = "Orcamentos e pagamentos das ordens de servico (aprovacao, Mercado Pago, simulador).",
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Bearer JWT emitido pela Lambda oficina-auth (perfil Cliente, Atendente ou Admin)",
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                    },
                    Array.Empty<string>()
                },
            });
        });
        return services;
    }
}
