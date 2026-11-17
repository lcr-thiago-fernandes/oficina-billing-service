using System.Collections.Concurrent;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Api.Testes;

/// <summary>
/// Repositorio em memoria para os testes HTTP (sem DynamoDB). Reproduz o contrato da porta: desfecho atomico com
/// dedupe por registro de webhook e marcacao do evento publicado.
/// </summary>
public sealed class RepositorioBillingEmMemoria : IRepositorioBilling
{
    private readonly object _trava = new();

    public ConcurrentDictionary<Guid, Orcamento> Orcamentos { get; } = new();
    public ConcurrentDictionary<string, Pagamento> Pagamentos { get; } = new();
    public ConcurrentDictionary<string, RegistroWebhook> Webhooks { get; } = new();

    /// <summary>true: <see cref="RegistrarDesfechoAsync"/> lanca <see cref="ConcorrenciaException"/>.</summary>
    public bool SimularConcorrenciaNoDesfecho { get; set; }

    public void Semear(Orcamento orcamento) => Orcamentos[orcamento.OrdemServicoId] = orcamento;

    public Task<bool> InserirOrcamentoAsync(Orcamento orcamento, CancellationToken ct) =>
        Task.FromResult(Orcamentos.TryAdd(orcamento.OrdemServicoId, orcamento));

    public Task<Orcamento?> ObterOrcamentoAsync(Guid ordemServicoId, CancellationToken ct) =>
        Task.FromResult(Orcamentos.GetValueOrDefault(ordemServicoId));

    public Task AtualizarOrcamentoAsync(Orcamento orcamento, CancellationToken ct)
    {
        orcamento.IncrementarVersao();
        Orcamentos[orcamento.OrdemServicoId] = orcamento;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Pagamento>> ListarPagamentosAsync(Guid ordemServicoId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Pagamento>>(
            Pagamentos.Values.Where(p => p.OrdemServicoId == ordemServicoId).ToList());

    public Task SalvarPagamentoAsync(Pagamento pagamento, bool novo, CancellationToken ct)
    {
        Pagamentos[pagamento.Id] = pagamento;
        return Task.CompletedTask;
    }

    public Task<RegistroWebhook?> ObterRegistroWebhookAsync(string pagamentoId, CancellationToken ct) =>
        Task.FromResult(Webhooks.GetValueOrDefault(pagamentoId));

    public Task<bool> RegistrarDesfechoAsync(DesfechoPagamento desfecho, CancellationToken ct)
    {
        if (SimularConcorrenciaNoDesfecho)
            throw new ConcorrenciaException("Conflito de versao simulado");

        lock (_trava)
        {
            if (!Webhooks.TryAdd(desfecho.Webhook.PagamentoId, desfecho.Webhook))
                return Task.FromResult(false);
            Pagamentos[desfecho.Pagamento.Id] = desfecho.Pagamento;
            if (desfecho.OrcamentoAlterado is { } orcamento)
            {
                orcamento.IncrementarVersao();
                Orcamentos[orcamento.OrdemServicoId] = orcamento;
            }
            return Task.FromResult(true);
        }
    }

    public Task MarcarEventoPublicadoAsync(string pagamentoId, CancellationToken ct)
    {
        if (Webhooks.TryGetValue(pagamentoId, out var registro))
            Webhooks[pagamentoId] = registro with { EventoPublicado = true };
        return Task.CompletedTask;
    }
}
