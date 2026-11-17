using Microsoft.Extensions.Logging;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;

namespace Oficina.Billing.Aplicacao.CasosDeUso;

public class CancelarOrcamentoCasoDeUso(IRepositorioBilling repositorio, ITelemetriaPagamento telemetria,
    TimeProvider relogio, ILogger<CancelarOrcamentoCasoDeUso> logger)
{
    public async Task ExecutarAsync(CancelarOrcamento comando, CancellationToken ct)
    {
        var orcamento = await repositorio.ObterOrcamentoAsync(comando.OrdemServicoId, ct);
        if (orcamento is null)
        {
            logger.LogWarning("Cancelamento ignorado: orcamento da OS {OrdemServicoId} nao existe", comando.OrdemServicoId);
            return;
        }

        if (!orcamento.Cancelar(comando.Motivo, relogio.GetUtcNow().UtcDateTime))
            return; // ja em estado terminal (Cancelado/Rejeitado): idempotente

        await repositorio.AtualizarOrcamentoAsync(orcamento, ct);
        telemetria.Registrar(new EventoPagamento(orcamento.OrdemServicoId, orcamento.NumeroOs, "OrcamentoCancelado",
            "Sucesso", orcamento.ValorTotal, null, "-"));
    }
}
