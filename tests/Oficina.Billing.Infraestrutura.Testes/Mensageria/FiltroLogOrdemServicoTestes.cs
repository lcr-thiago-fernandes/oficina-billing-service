using MassTransit;
using Moq;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Infraestrutura.Mensageria;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Oficina.Billing.Infraestrutura.Testes.Mensageria;

public class FiltroLogOrdemServicoTestes
{
    private sealed class SinkEmMemoria : ILogEventSink
    {
        public List<LogEvent> Eventos { get; } = [];
        public void Emit(LogEvent logEvent) => Eventos.Add(logEvent);
    }

    private static async Task<LogEvent> ExecutarELogarAsync<T>(T mensagem, Guid? conversationId) where T : class
    {
        var sink = new SinkEmMemoria();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        var contexto = new Mock<ConsumeContext<T>>();
        contexto.SetupGet(c => c.Message).Returns(mensagem);
        contexto.SetupGet(c => c.ConversationId).Returns(conversationId);

        await new FiltroLogOrdemServico<T>().Send(contexto.Object,
            Pipe.Execute<ConsumeContext<T>>(_ => logger.Information("consumindo")));

        return sink.Eventos.Single();
    }

    [Fact]
    public async Task MensagemDaSaga_EmpilhaOrdemServicoIdECorrelationId()
    {
        var osId = Guid.NewGuid();
        var conversa = Guid.NewGuid();

        var evento = await ExecutarELogarAsync(new CancelarOrcamento { OrdemServicoId = osId, Motivo = "x" }, conversa);

        evento.Properties["ordemServicoId"].ToString().Should().Contain(osId.ToString());
        evento.Properties["correlationId"].ToString().Should().Contain(conversa.ToString());
    }

    [Fact]
    public async Task MensagemForaDaSaga_NaoEmpilhaPropriedades()
    {
        var evento = await ExecutarELogarAsync(new MensagemQualquer(), Guid.NewGuid());

        evento.Properties.Should().NotContainKey("ordemServicoId").And.NotContainKey("correlationId");
    }

    [Fact]
    public async Task AposOConsumo_PropriedadesSaemDoContexto()
    {
        await ExecutarELogarAsync(new CancelarOrcamento { OrdemServicoId = Guid.NewGuid(), Motivo = "x" }, Guid.NewGuid());
        var sink = new SinkEmMemoria();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();

        logger.Information("depois");

        sink.Eventos.Single().Properties.Should().NotContainKey("ordemServicoId");
    }

    public sealed class MensagemQualquer;
}
