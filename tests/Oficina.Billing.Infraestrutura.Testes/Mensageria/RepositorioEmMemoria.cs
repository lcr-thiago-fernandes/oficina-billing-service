using System.Collections.Concurrent;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Infraestrutura.Testes.Mensageria;

/// <summary>Repositorio em memoria para os testes de mensageria (sem DynamoDB). Conta as insercoes efetivas.</summary>
internal class RepositorioEmMemoria : IRepositorioBilling
{
    public ConcurrentDictionary<Guid, Orcamento> Orcamentos { get; } = new();
    public ConcurrentDictionary<string, Pagamento> Pagamentos { get; } = new();
    public int Insercoes;

    public virtual Task<bool> InserirOrcamentoAsync(Orcamento orcamento, CancellationToken ct)
    {
        var inserido = Orcamentos.TryAdd(orcamento.OrdemServicoId, orcamento);
        if (inserido)
            Interlocked.Increment(ref Insercoes);
        return Task.FromResult(inserido);
    }

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
        Task.FromResult<RegistroWebhook?>(null);

    public Task<bool> RegistrarDesfechoAsync(DesfechoPagamento desfecho, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task MarcarEventoPublicadoAsync(string pagamentoId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Repositorio que sempre falha ao inserir: forca o consumidor de <c>GerarOrcamento</c> a lancar.</summary>
internal sealed class RepositorioQueLanca : RepositorioEmMemoria
{
    public override Task<bool> InserirOrcamentoAsync(Orcamento orcamento, CancellationToken ct) =>
        throw new InvalidOperationException("falha simulada do repositorio");
}
