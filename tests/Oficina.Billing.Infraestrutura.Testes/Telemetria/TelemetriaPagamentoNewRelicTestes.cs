using Microsoft.Extensions.Logging;
using Moq;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Infraestrutura.Telemetria;

namespace Oficina.Billing.Infraestrutura.Testes.Telemetria;

public class TelemetriaPagamentoNewRelicTestes
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(191.00, "pix")]
    public void Registrar_SemAgente_NaoLanca(double? valor, string? meio)
    {
        // Sem o profiler do agente a API do New Relic e no-op; nada pode propagar nem gerar aviso.
        var logger = new Mock<ILogger<TelemetriaPagamentoNewRelic>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var telemetria = new TelemetriaPagamentoNewRelic(logger.Object);

        var acao = () => telemetria.Registrar(new EventoPagamento(Guid.NewGuid(), 7, "PagamentoConfirmado", "Sucesso",
            (decimal?)valor, meio, "Simulado"));

        acao.Should().NotThrow();
        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Fact]
    public void NomeEvento_PagamentoEvento() => TelemetriaPagamentoNewRelic.NomeEvento.Should().Be("PagamentoEvento");

    [Fact]
    public void MontarAtributos_SemValorEMeio_OmiteAsChaves()
    {
        var osId = Guid.NewGuid();

        var atributos = TelemetriaPagamentoNewRelic.MontarAtributos(
            new EventoPagamento(osId, 7, "OrcamentoFalhou", "Falha", null, null, "-"));

        atributos.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["ordemServicoId"] = osId.ToString(),
            ["numeroOs"] = 7,
            ["etapa"] = "OrcamentoFalhou",
            ["resultado"] = "Falha",
            ["gateway"] = "-",
        });
    }

    [Fact]
    public void MontarAtributos_ComValorEMeio_IncluiAsChaves()
    {
        var atributos = TelemetriaPagamentoNewRelic.MontarAtributos(
            new EventoPagamento(Guid.NewGuid(), 7, "PagamentoConfirmado", "Sucesso", 191.00m, "pix", "MercadoPago"));

        atributos["valor"].Should().Be(191.00m);
        atributos["meio"].Should().Be("pix");
        atributos["gateway"].Should().Be("MercadoPago");
    }
}
