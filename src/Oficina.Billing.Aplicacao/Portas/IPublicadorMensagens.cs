namespace Oficina.Billing.Aplicacao.Portas;

public interface IPublicadorMensagens
{
    Task PublicarAsync<T>(T mensagem, CancellationToken ct) where T : class;
}
