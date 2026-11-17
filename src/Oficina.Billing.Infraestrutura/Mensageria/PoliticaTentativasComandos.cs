namespace Oficina.Billing.Infraestrutura.Mensageria;

/// <summary>
/// Limites de retry imediato e de redelivery atrasado da fila de comandos, registrados por
/// <see cref="ConfiguracaoMensageria.ConfigurarRegistro"/> com os mesmos valores usados no bus.
/// </summary>
/// <param name="LimiteRetry">Retries imediatos por entrega (<c>UseMessageRetry</c>).</param>
/// <param name="LimiteRedelivery">Redeliveries atrasados (<c>UseDelayedRedelivery</c>, so SQS); 0 sem redelivery.</param>
public sealed record PoliticaTentativasComandos(int LimiteRetry, int LimiteRedelivery)
{
    /// <summary>
    /// Ultima tentativa = ultimo retry imediato da ultima redelivery. O contador de retry reinicia a cada redelivery,
    /// entao olhar so o retry encerraria a mensagem na primeira entrega e pularia o redelivery atrasado.
    /// </summary>
    public bool EhUltimaTentativa(int tentativaRetry, int contagemRedelivery) =>
        tentativaRetry >= LimiteRetry && contagemRedelivery >= LimiteRedelivery;
}
