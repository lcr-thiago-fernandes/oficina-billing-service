namespace Oficina.Billing.Dominio.Comum;

public abstract class DominioException : Exception
{
    protected DominioException(string mensagem) : base(mensagem) { }
}
