using Microsoft.Extensions.DependencyInjection;
using Oficina.Billing.Aplicacao.CasosDeUso;

namespace Oficina.Billing.Aplicacao;

public static class InjecaoDeDependencia
{
    public static IServiceCollection AdicionarAplicacao(this IServiceCollection servicos)
    {
        servicos.AddSingleton(TimeProvider.System);
        servicos.AddScoped<GerarOrcamentoCasoDeUso>();
        servicos.AddScoped<ConsultarOrcamentoCasoDeUso>();
        servicos.AddScoped<AprovarOrcamentoCasoDeUso>();
        servicos.AddScoped<RejeitarOrcamentoCasoDeUso>();
        servicos.AddScoped<CancelarOrcamentoCasoDeUso>();
        servicos.AddScoped<ProcessarNotificacaoPagamentoCasoDeUso>();
        servicos.AddScoped<EstornarPagamentoCasoDeUso>();
        servicos.AddScoped<ConsultarPagamentosCasoDeUso>();
        return servicos;
    }
}
