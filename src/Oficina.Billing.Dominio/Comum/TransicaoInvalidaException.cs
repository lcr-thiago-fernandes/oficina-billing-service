namespace Oficina.Billing.Dominio.Comum;

public sealed class TransicaoInvalidaException : DominioException
{
    public TransicaoInvalidaException(string entidade, string de, string para)
        : base($"Transicao invalida em {entidade}: {de} -> {para}")
    {
        Entidade = entidade;
        De = de;
        Para = para;
    }

    public string Entidade { get; }
    public string De { get; }
    public string Para { get; }
}
