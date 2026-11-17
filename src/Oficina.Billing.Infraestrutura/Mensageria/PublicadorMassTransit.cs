using MassTransit;
using Oficina.Billing.Aplicacao.Portas;

namespace Oficina.Billing.Infraestrutura.Mensageria;

/// <summary>Scoped: dentro de um consumidor, o <see cref="IPublishEndpoint"/> e o proprio ConsumeContext (mantem ConversationId).</summary>
public class PublicadorMassTransit(IPublishEndpoint publicador) : IPublicadorMensagens
{
    public Task PublicarAsync<T>(T mensagem, CancellationToken ct) where T : class =>
        publicador.Publish(mensagem, ct);
}
