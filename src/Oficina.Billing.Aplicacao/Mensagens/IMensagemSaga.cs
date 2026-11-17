using MassTransit;

namespace Oficina.Billing.Aplicacao.Mensagens;

/// <summary>Campos comuns de toda mensagem da saga. Excluida da topologia para nao criar exchange propria.</summary>
[ExcludeFromTopology]
public interface IMensagemSaga
{
    Guid OrdemServicoId { get; }
    DateTime OcorridoEm { get; }
}
