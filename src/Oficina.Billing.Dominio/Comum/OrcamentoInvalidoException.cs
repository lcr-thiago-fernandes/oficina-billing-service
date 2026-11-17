namespace Oficina.Billing.Dominio.Comum;

public sealed class OrcamentoInvalidoException : DominioException
{
    public OrcamentoInvalidoException(string motivo) : base(motivo) => Motivo = motivo;

    public string Motivo { get; }
}
