using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Aplicacao.CasosDeUso;

public class RejeitarOrcamentoCasoDeUso(IRepositorioBilling repositorio, IPublicadorMensagens publicador,
    ITelemetriaPagamento telemetria, TimeProvider relogio)
{
    public async Task<OrcamentoResposta> ExecutarAsync(Guid ordemServicoId, Solicitante solicitante, string? motivo,
        CancellationToken ct)
    {
        var orcamento = await repositorio.ObterOrcamentoAsync(ordemServicoId, ct);
        if (orcamento is null || !solicitante.PodeAcessar(orcamento.Cliente.Documento))
            throw new OrcamentoNaoEncontradoException(ordemServicoId);

        var agora = relogio.GetUtcNow().UtcDateTime;

        if (orcamento.Status != StatusOrcamento.Rejeitado)
        {
            orcamento.Rejeitar(motivo, agora); // lanca TransicaoInvalidaException fora de Gerado
            await repositorio.AtualizarOrcamentoAsync(orcamento, ct);
        }

        await publicador.PublicarAsync(new OrcamentoRejeitado
        {
            OrdemServicoId = orcamento.OrdemServicoId, OcorridoEm = agora, OrcamentoId = orcamento.Id,
            Motivo = orcamento.Motivo,
        }, ct);
        telemetria.Registrar(new EventoPagamento(orcamento.OrdemServicoId, orcamento.NumeroOs, "OrcamentoRejeitado",
            "Sucesso", orcamento.ValorTotal, null, "-"));
        return OrcamentoResposta.De(orcamento);
    }
}
