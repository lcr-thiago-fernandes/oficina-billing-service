using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Aplicacao.CasosDeUso;

/// <summary>Compensacao da saga: estorna o pagamento aprovado da OS e cancela o orcamento.</summary>
public class EstornarPagamentoCasoDeUso(IRepositorioBilling repositorio, IGatewayPagamento gateway,
    IPublicadorMensagens publicador, ITelemetriaPagamento telemetria, TimeProvider relogio)
{
    public const string MotivoSemPagamento = "Nenhum pagamento aprovado para a OS";
    public const string MotivoGatewayIndisponivel = "Mercado Pago indisponivel apos 3 tentativas";

    /// <param name="ultimaTentativa">true na ultima entrega do retry: falha do gateway vira <c>EstornoFalhou</c> em vez de excecao.</param>
    public async Task ExecutarAsync(EstornarPagamento comando, bool ultimaTentativa, CancellationToken ct)
    {
        var osId = comando.OrdemServicoId;
        var pagamentos = await repositorio.ListarPagamentosAsync(osId, ct);
        var orcamento = await repositorio.ObterOrcamentoAsync(osId, ct);
        var numeroOs = orcamento?.NumeroOs ?? 0;

        var pagamento = pagamentos.FirstOrDefault(p => p.Status == StatusPagamento.Aprovado);
        if (pagamento is not null)
        {
            EstornoGateway estorno;
            try
            {
                estorno = await gateway.EstornarAsync(pagamento.Id, pagamento.Valor, $"estorno-{pagamento.Id}", ct);
            }
            catch (GatewayPagamentoIndisponivelException) when (ultimaTentativa)
            {
                await PublicarFalhaAsync(osId, numeroOs, MotivoGatewayIndisponivel, pagamento.Valor, ct);
                return;
            }

            var agora = relogio.GetUtcNow().UtcDateTime;
            pagamento.Estornar(estorno.EstornoId, agora);
            await repositorio.SalvarPagamentoAsync(pagamento, novo: false, ct);
        }
        else
        {
            // Ja estornado (reentrega apos sucesso): republica sem chamar o gateway de novo.
            pagamento = pagamentos.FirstOrDefault(p => p.Status == StatusPagamento.Estornado);
            if (pagamento is null)
            {
                await PublicarFalhaAsync(osId, numeroOs, MotivoSemPagamento, null, ct);
                return;
            }
        }

        // Tambem na reentrega: conclui o cancelamento caso a queda tenha ocorrido antes dele.
        if (orcamento is not null && orcamento.Cancelar(comando.Motivo, relogio.GetUtcNow().UtcDateTime))
            await repositorio.AtualizarOrcamentoAsync(orcamento, ct);

        await publicador.PublicarAsync(new PagamentoEstornado
        {
            OrdemServicoId = osId,
            OcorridoEm = relogio.GetUtcNow().UtcDateTime,
            PagamentoId = pagamento.Id,
            Valor = pagamento.Valor,
        }, ct);
        telemetria.Registrar(new EventoPagamento(osId, numeroOs, "PagamentoEstornado", "Sucesso", pagamento.Valor,
            pagamento.Meio, gateway.Nome));
    }

    private async Task PublicarFalhaAsync(Guid osId, int numeroOs, string motivo, decimal? valor, CancellationToken ct)
    {
        await publicador.PublicarAsync(new EstornoFalhou
        {
            OrdemServicoId = osId,
            OcorridoEm = relogio.GetUtcNow().UtcDateTime,
            Motivo = motivo,
        }, ct);
        telemetria.Registrar(new EventoPagamento(osId, numeroOs, "EstornoFalhou", "Falha", valor, null, gateway.Nome));
    }
}
