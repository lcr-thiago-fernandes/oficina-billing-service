using MassTransit;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;

namespace Oficina.Billing.Infraestrutura.Mensageria.Consumidores;

public class GerarOrcamentoConsumidor(GerarOrcamentoCasoDeUso casoDeUso) : IConsumer<GerarOrcamento>
{
    public Task Consume(ConsumeContext<GerarOrcamento> context) =>
        casoDeUso.ExecutarAsync(context.Message, context.CancellationToken);
}
