using MassTransit;
using Oficina.Billing.Aplicacao.Mensagens;
using LogContext = Serilog.Context.LogContext;

namespace Oficina.Billing.Infraestrutura.Mensageria;

/// <summary>Enriquece os logs do consumo com <c>ordemServicoId</c> e <c>correlationId</c> (ConversationId da saga).</summary>
public class FiltroLogOrdemServico<T> : IFilter<ConsumeContext<T>> where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (context.Message is not IMensagemSaga mensagem)
        {
            await next.Send(context);
            return;
        }

        using (LogContext.PushProperty("ordemServicoId", mensagem.OrdemServicoId))
        using (LogContext.PushProperty("correlationId", context.ConversationId))
        {
            await next.Send(context);
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("logOrdemServico");
}
