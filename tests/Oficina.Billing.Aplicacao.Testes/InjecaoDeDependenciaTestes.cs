using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Portas;

namespace Oficina.Billing.Aplicacao.Testes;

public class InjecaoDeDependenciaTestes
{
    [Fact]
    public void AdicionarAplicacao_ResolveOsOitoCasosDeUso()
    {
        var servicos = new ServiceCollection();
        servicos.AddSingleton(new Mock<IRepositorioBilling>().Object);
        servicos.AddSingleton(new Mock<IGatewayPagamento>().Object);
        servicos.AddSingleton(new Mock<IPublicadorMensagens>().Object);
        servicos.AddSingleton(new Mock<ITelemetriaPagamento>().Object);
        servicos.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        servicos.AdicionarAplicacao();

        using var provedor = servicos.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        using var escopo = provedor.CreateScope();
        var sp = escopo.ServiceProvider;

        sp.GetRequiredService<GerarOrcamentoCasoDeUso>().Should().NotBeNull();
        sp.GetRequiredService<ConsultarOrcamentoCasoDeUso>().Should().NotBeNull();
        sp.GetRequiredService<AprovarOrcamentoCasoDeUso>().Should().NotBeNull();
        sp.GetRequiredService<RejeitarOrcamentoCasoDeUso>().Should().NotBeNull();
        sp.GetRequiredService<CancelarOrcamentoCasoDeUso>().Should().NotBeNull();
        sp.GetRequiredService<ProcessarNotificacaoPagamentoCasoDeUso>().Should().NotBeNull();
        sp.GetRequiredService<EstornarPagamentoCasoDeUso>().Should().NotBeNull();
        sp.GetRequiredService<ConsultarPagamentosCasoDeUso>().Should().NotBeNull();
    }
}
