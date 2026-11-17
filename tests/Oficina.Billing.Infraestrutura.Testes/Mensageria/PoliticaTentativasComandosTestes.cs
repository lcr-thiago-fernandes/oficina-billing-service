using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Oficina.Billing.Infraestrutura.Mensageria;

namespace Oficina.Billing.Infraestrutura.Testes.Mensageria;

public class PoliticaTentativasComandosTestes
{
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(3, 0, false)] // ultimo retry da 1a entrega: ainda ha redelivery atrasado
    [InlineData(3, 2, false)]
    [InlineData(2, 3, false)] // ultima redelivery, mas ainda ha retry imediato
    [InlineData(3, 3, true)]
    public void ComRedelivery_SoNoUltimoRetryDaUltimaRedelivery(int retry, int redelivery, bool esperado) =>
        new PoliticaTentativasComandos(3, 3).EhUltimaTentativa(retry, redelivery).Should().Be(esperado);

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void SemRedelivery_SoORetryConta(int retry, bool esperado) =>
        new PoliticaTentativasComandos(3, 0).EhUltimaTentativa(retry, 0).Should().Be(esperado);

    [Theory]
    [InlineData("AmazonSqs", 3)]
    [InlineData("InMemory", 0)]
    public void ConfigurarRegistro_RegistraPoliticaDoTransporte(string transporte, int limiteRedelivery)
    {
        var servicos = new ServiceCollection();
        servicos.AddMassTransit(x => ConfiguracaoMensageria.ConfigurarRegistro(x, new OpcoesMensageria
        {
            Transporte = transporte,
            PrefixoAmbiente = "test",
        }));

        using var provider = servicos.BuildServiceProvider();

        provider.GetRequiredService<PoliticaTentativasComandos>().Should()
            .Be(new PoliticaTentativasComandos(3, limiteRedelivery));
    }
}
