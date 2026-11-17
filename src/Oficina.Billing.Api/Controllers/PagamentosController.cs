using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oficina.Billing.Api.Configuracao;
using Oficina.Billing.Aplicacao.CasosDeUso;

namespace Oficina.Billing.Api.Controllers;

[ApiController]
[Route("api/v1/pagamentos")]
[Authorize(Policy = PoliticasDeAutorizacao.RequerClienteOuEquipe)]
[Produces("application/json")]
public class PagamentosController(ConsultarPagamentosCasoDeUso consultar) : ControllerBase
{
    [HttpGet("{osId:guid}")]
    [ProducesResponseType<IReadOnlyList<PagamentoResposta>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PagamentoResposta>>> Listar(Guid osId, CancellationToken ct) =>
        Ok(await consultar.ExecutarAsync(osId, User.Solicitante(), ct));
}
