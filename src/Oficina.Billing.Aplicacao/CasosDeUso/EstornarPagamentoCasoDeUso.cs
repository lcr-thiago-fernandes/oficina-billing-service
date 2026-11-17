using Microsoft.Extensions.Logging;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Aplicacao.CasosDeUso;

/// <summary>
/// Compensacao da saga: estorna o pagamento aprovado da OS e cancela o orcamento. A saga sempre recebe um desfecho:
/// <c>PagamentoEstornado</c> ou <c>EstornoFalhou</c> (nunca uma falha silenciosa, ja que <c>PublishFaults = false</c>).
/// </summary>
public class EstornarPagamentoCasoDeUso(IRepositorioBilling repositorio, IGatewayPagamento gateway,
    IPublicadorMensagens publicador, ITelemetriaPagamento telemetria, TimeProvider relogio,
    ILogger<EstornarPagamentoCasoDeUso> logger)
{
    public const string MotivoSemPagamento = "Nenhum pagamento aprovado para a OS";
    public const string MotivoGatewayIndisponivel = "Mercado Pago indisponivel apos 3 tentativas";
    public const string MotivoGatewayRecusou = "Mercado Pago recusou o estorno";
    public const string MotivoFalhaGenerica = "Falha ao processar o estorno apos 3 tentativas";

    /// <param name="ultimaTentativa">
    /// true na ultima entrega do retry: qualquer falha (exceto cancelamento) vira <c>EstornoFalhou</c> em vez de excecao.
    /// </param>
    public async Task ExecutarAsync(EstornarPagamento comando, bool ultimaTentativa, CancellationToken ct)
    {
        var osId = comando.OrdemServicoId;
        var numeroOs = 0;
        try
        {
            var pagamentos = await repositorio.ListarPagamentosAsync(osId, ct);
            var orcamento = await repositorio.ObterOrcamentoAsync(osId, ct);
            numeroOs = orcamento?.NumeroOs ?? 0;

            var pagamento = pagamentos.FirstOrDefault(p => p.Status == StatusPagamento.Aprovado);
            if (pagamento is not null)
            {
                EstornoGateway estorno;
                try
                {
                    estorno = await gateway.EstornarAsync(pagamento.Id, pagamento.Valor, $"estorno-{pagamento.Id}", ct);
                }
                catch (InvalidOperationException ex)
                {
                    // 4xx do gateway: nao adianta repetir.
                    logger.LogError(ex, "Gateway recusou o estorno do pagamento {PagamentoId} da OS {OrdemServicoId}",
                        pagamento.Id, osId);
                    await PublicarFalhaAsync(osId, numeroOs, MotivoGatewayRecusou, pagamento.Valor, ct);
                    return;
                }

                pagamento.Estornar(estorno.EstornoId, relogio.GetUtcNow().UtcDateTime);
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
        catch (Exception ex) when (ultimaTentativa && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Estorno da OS {OrdemServicoId} falhou na ultima tentativa", osId);
            var motivo = ex is GatewayPagamentoIndisponivelException ? MotivoGatewayIndisponivel : MotivoFalhaGenerica;
            await PublicarFalhaAsync(osId, numeroOs, motivo, null, ct);
        }
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
