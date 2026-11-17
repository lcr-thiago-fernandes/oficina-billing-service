using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Aplicacao.Portas;

public record DesfechoPagamento(RegistroWebhook Webhook, Pagamento Pagamento, bool PagamentoNovo,
    Orcamento? OrcamentoAlterado);
