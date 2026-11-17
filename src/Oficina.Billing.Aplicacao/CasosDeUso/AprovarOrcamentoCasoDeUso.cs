using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Comum;
using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Aplicacao.CasosDeUso;

public class AprovarOrcamentoCasoDeUso(IRepositorioBilling repositorio, IGatewayPagamento gateway,
    IPublicadorMensagens publicador, ITelemetriaPagamento telemetria, TimeProvider relogio)
{
    public async Task<OrcamentoResposta> ExecutarAsync(Guid ordemServicoId, Solicitante solicitante, CancellationToken ct)
    {
        var orcamento = await repositorio.ObterOrcamentoAsync(ordemServicoId, ct);
        if (orcamento is null || !solicitante.PodeAcessar(orcamento.Cliente.Documento))
            throw new OrcamentoNaoEncontradoException(ordemServicoId);

        var agora = relogio.GetUtcNow().UtcDateTime;

        if (orcamento.Status == StatusOrcamento.Gerado)
        {
            var cobranca = await gateway.CriarCobrancaAsync(new NovaCobranca(orcamento.OrdemServicoId,
                orcamento.NumeroOs, orcamento.Cliente.Email,
                orcamento.Itens.Select(i => new ItemCobranca(i.Descricao, i.Quantidade, i.PrecoUnitario)).ToList()), ct);
            orcamento.Aprovar(cobranca.PreferenciaId, cobranca.LinkPagamento, agora);
            await repositorio.AtualizarOrcamentoAsync(orcamento, ct);
        }
        else if (orcamento.Status != StatusOrcamento.Aprovado)
        {
            throw new TransicaoInvalidaException(nameof(Orcamento), orcamento.Status.ToString(),
                StatusOrcamento.Aprovado.ToString());
        }

        // Aprovado ja existente: apenas republica (a queda pode ter ocorrido entre gravar e publicar).
        await publicador.PublicarAsync(new OrcamentoAprovado
        {
            OrdemServicoId = orcamento.OrdemServicoId, OcorridoEm = agora, OrcamentoId = orcamento.Id,
            LinkPagamento = orcamento.LinkPagamento!,
        }, ct);
        telemetria.Registrar(new EventoPagamento(orcamento.OrdemServicoId, orcamento.NumeroOs, "OrcamentoAprovado",
            "Sucesso", orcamento.ValorTotal, null, gateway.Nome));
        return OrcamentoResposta.De(orcamento);
    }
}
