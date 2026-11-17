namespace Oficina.Billing.Aplicacao.CasosDeUso;

public sealed class OrcamentoNaoEncontradoException : Exception
{
    public OrcamentoNaoEncontradoException(Guid ordemServicoId)
        : base($"Orcamento da OS {ordemServicoId} nao encontrado") => OrdemServicoId = ordemServicoId;

    public Guid OrdemServicoId { get; }
}

public sealed class ConcorrenciaException : Exception
{
    public ConcorrenciaException(string mensagem) : base(mensagem) { }

    public ConcorrenciaException(string mensagem, Exception? interna) : base(mensagem, interna) { }
}
