using MassTransit;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Mensagens;

namespace Oficina.Billing.Infraestrutura.Mensageria.Consumidores;

public class EstornarPagamentoConsumidor(EstornarPagamentoCasoDeUso casoDeUso, PoliticaTentativasComandos politica)
    : IConsumer<EstornarPagamento>
{
    /// <summary>Igual ao limite do <c>UseMessageRetry</c> da fila de comandos.</summary>
    public const int LimiteRetry = 3;

    /// <summary>Igual ao numero de intervalos do <c>UseDelayedRedelivery</c> da fila de comandos (SQS).</summary>
    public const int LimiteRedelivery = 3;

    /// <remarks>
    /// So na ultima tentativa (ultimo retry da ultima redelivery) o caso de uso converte a falha em <c>EstornoFalhou</c>
    /// (a saga sempre recebe desfecho); antes disso a falha propaga e o bus tenta de novo.
    /// </remarks>
    public Task Consume(ConsumeContext<EstornarPagamento> context) =>
        casoDeUso.ExecutarAsync(context.Message,
            ultimaTentativa: politica.EhUltimaTentativa(context.GetRetryAttempt(), context.GetRedeliveryCount()),
            context.CancellationToken);
}
