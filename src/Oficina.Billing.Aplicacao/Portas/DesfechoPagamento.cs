using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Aplicacao.Portas;

/// <param name="OrcamentoAlterado">
/// Orcamento gravado na mesma transacao com condicao de versao: o cancelado pelo desfecho, ou o lido (sem alteracao)
/// numa confirmacao, como guarda contra duas confirmacoes simultaneas da mesma OS. Nulo: orcamento fora da transacao.
/// </param>
public record DesfechoPagamento(RegistroWebhook Webhook, Pagamento Pagamento, bool PagamentoNovo,
    Orcamento? OrcamentoAlterado);
