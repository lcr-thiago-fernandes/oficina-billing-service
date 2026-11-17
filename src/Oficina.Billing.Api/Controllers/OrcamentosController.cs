using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Oficina.Billing.Api.Configuracao;
using Oficina.Billing.Aplicacao.CasosDeUso;

namespace Oficina.Billing.Api.Controllers;

public record RejeicaoRequisicao(string? Motivo);

[ApiController]
[Route("api/v1/orcamentos")]
[Authorize(Policy = PoliticasDeAutorizacao.RequerClienteOuEquipe)]
[Produces("application/json")]
public class OrcamentosController(
    ConsultarOrcamentoCasoDeUso consultar,
    AprovarOrcamentoCasoDeUso aprovar,
    RejeitarOrcamentoCasoDeUso rejeitar) : ControllerBase
{
    [HttpGet("{osId:guid}")]
    [ProducesResponseType<OrcamentoResposta>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrcamentoResposta>> Obter(Guid osId, CancellationToken ct) =>
        Ok(await consultar.ExecutarAsync(osId, User.Solicitante(), ct));

    /// <summary>Aprova o orcamento e devolve o link de pagamento (idempotente: repetir devolve o mesmo link).</summary>
    [HttpPost("{osId:guid}/aprovacao")]
    [ProducesResponseType<OrcamentoResposta>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<OrcamentoResposta>> Aprovar(Guid osId, CancellationToken ct) =>
        Ok(await aprovar.ExecutarAsync(osId, User.Solicitante(), ct));

    [HttpPost("{osId:guid}/rejeicao")]
    [ProducesResponseType<OrcamentoResposta>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrcamentoResposta>> Rejeitar(Guid osId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RejeicaoRequisicao? corpo, CancellationToken ct) =>
        Ok(await rejeitar.ExecutarAsync(osId, User.Solicitante(), corpo?.Motivo, ct));
}
