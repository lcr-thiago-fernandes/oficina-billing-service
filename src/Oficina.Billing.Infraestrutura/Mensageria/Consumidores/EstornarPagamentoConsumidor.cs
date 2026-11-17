using MassTransit;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;

namespace Oficina.Billing.Infraestrutura.Mensageria.Consumidores;

public class EstornarPagamentoConsumidor(EstornarPagamentoCasoDeUso casoDeUso) : IConsumer<EstornarPagamento>
{
    /// <summary>Igual ao limite do <c>UseMessageRetry</c> da fila de comandos.</summary>
    public const int LimiteRetry = 3;

    /// <remarks>Na ultima tentativa o caso de uso converte a falha em <c>EstornoFalhou</c> (a saga sempre recebe desfecho).</remarks>
    public Task Consume(ConsumeContext<EstornarPagamento> context) =>
        casoDeUso.ExecutarAsync(context.Message, ultimaTentativa: context.GetRetryAttempt() >= LimiteRetry,
            context.CancellationToken);
}
