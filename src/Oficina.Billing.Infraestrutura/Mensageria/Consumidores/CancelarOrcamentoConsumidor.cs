using MassTransit;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;

namespace Oficina.Billing.Infraestrutura.Mensageria.Consumidores;

public class CancelarOrcamentoConsumidor(CancelarOrcamentoCasoDeUso casoDeUso) : IConsumer<CancelarOrcamento>
{
    public Task Consume(ConsumeContext<CancelarOrcamento> context) =>
        casoDeUso.ExecutarAsync(context.Message, context.CancellationToken);
}
