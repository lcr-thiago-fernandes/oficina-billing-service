using Oficina.Billing.Aplicacao.Portas;

namespace Oficina.Billing.Aplicacao.CasosDeUso;

public class ConsultarOrcamentoCasoDeUso(IRepositorioBilling repositorio)
{
    public async Task<OrcamentoResposta> ExecutarAsync(Guid ordemServicoId, Solicitante solicitante, CancellationToken ct)
    {
        var orcamento = await repositorio.ObterOrcamentoAsync(ordemServicoId, ct);
        // 404 tambem para nao-dono: nao revela a existencia (decisao D3).
        if (orcamento is null || !solicitante.PodeAcessar(orcamento.Cliente.Documento))
            throw new OrcamentoNaoEncontradoException(ordemServicoId);
        return OrcamentoResposta.De(orcamento);
    }
}
