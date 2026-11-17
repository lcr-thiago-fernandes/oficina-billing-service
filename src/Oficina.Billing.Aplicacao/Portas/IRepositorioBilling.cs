using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Aplicacao.Portas;

public interface IRepositorioBilling
{
    /// <returns>false se o orcamento da OS ja existe.</returns>
    Task<bool> InserirOrcamentoAsync(Orcamento orcamento, CancellationToken ct);

    Task<Orcamento?> ObterOrcamentoAsync(Guid ordemServicoId, CancellationToken ct);

    /// <summary>Condicao <c>versao = o.Versao</c>, grava <c>Versao + 1</c> e chama <c>IncrementarVersao()</c>. Conflito: <c>ConcorrenciaException</c>.</summary>
    Task AtualizarOrcamentoAsync(Orcamento orcamento, CancellationToken ct);

    Task<IReadOnlyList<Pagamento>> ListarPagamentosAsync(Guid ordemServicoId, CancellationToken ct);

    /// <param name="novo">true: <c>attribute_not_exists(pk)</c>; false: condicao de versao.</param>
    Task SalvarPagamentoAsync(Pagamento pagamento, bool novo, CancellationToken ct);

    Task<RegistroWebhook?> ObterRegistroWebhookAsync(string pagamentoId, CancellationToken ct);

    /// <returns>false se o registro de webhook ja existe (transacao nao aplicada).</returns>
    Task<bool> RegistrarDesfechoAsync(DesfechoPagamento desfecho, CancellationToken ct);

    Task MarcarEventoPublicadoAsync(string pagamentoId, CancellationToken ct);
}
