using Microsoft.Extensions.Logging;
using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Aplicacao.CasosDeUso;

/// <summary>
/// Processa a notificacao (webhook) de um pagamento de forma idempotente: o registro de webhook e gravado na mesma
/// transacao do pagamento e o evento so e marcado como publicado depois de publicado, de modo que uma reentrega
/// apos queda republica o mesmo evento sem segunda escrita nem segundo estorno.
/// </summary>
public class ProcessarNotificacaoPagamentoCasoDeUso(IRepositorioBilling repositorio, IGatewayPagamento gateway,
    IPublicadorMensagens publicador, ITelemetriaPagamento telemetria, TimeProvider relogio,
    ILogger<ProcessarNotificacaoPagamentoCasoDeUso> logger)
{
    private const string EventoConfirmado = "PagamentoConfirmado";
    private const string EventoRecusado = "PagamentoRecusado";
    private const string EventoNenhum = "Nenhum";
    private const string MotivoValorDivergente = "valor_divergente";
    private const string MotivoRecusaPadrao = "rejected";

    public async Task<ResultadoNotificacao> ExecutarAsync(string pagamentoId, CancellationToken ct)
    {
        // 1. Estado real no gateway (o webhook so traz o id).
        var pg = await gateway.ObterPagamentoAsync(pagamentoId, ct);
        if (pg is null || pg.OrdemServicoId is null)
        {
            logger.LogWarning("Notificacao ignorada: pagamento {PagamentoId} inexistente ou sem OS", pagamentoId);
            return ResultadoNotificacao.Ignorado;
        }

        var ordemServicoId = pg.OrdemServicoId.Value;
        var orcamento = await repositorio.ObterOrcamentoAsync(ordemServicoId, ct);
        if (orcamento is null)
        {
            logger.LogWarning("Notificacao ignorada: orcamento da OS {OrdemServicoId} (pagamento {PagamentoId}) nao existe",
                ordemServicoId, pagamentoId);
            return ResultadoNotificacao.Ignorado;
        }

        var agora = relogio.GetUtcNow().UtcDateTime;

        // 2. Pendente / Estornado.
        if (pg.Status == StatusPagamentoGateway.Estornado)
            return ResultadoNotificacao.Ignorado; // o PagamentoEstornado e publicado pelo fluxo de estorno

        var existente = await ObterPagamentoSalvoAsync(ordemServicoId, pagamentoId, ct);
        if (pg.Status == StatusPagamentoGateway.Pendente)
        {
            if (existente is null)
            {
                await repositorio.SalvarPagamentoAsync(Pagamento.Registrar(pagamentoId, ordemServicoId, pg.Valor,
                    pg.Meio, StatusPagamento.Pendente, pg.DetalheStatus, agora), novo: true, ct);
            }
            return ResultadoNotificacao.Pendente;
        }

        // 3. Ja processado: republica se a publicacao anterior nao foi concluida.
        var registro = await repositorio.ObterRegistroWebhookAsync(pagamentoId, ct);
        if (registro is not null)
            return await TratarDuplicadoAsync(registro, pg, existente, ct);
        if (existente is not null && existente.Status != StatusPagamento.Pendente)
        {
            // Desfecho ja gravado sem registro de webhook (ex.: registro expirado pelo TTL): nada a decidir de novo.
            logger.LogWarning("Notificacao ignorada: pagamento {PagamentoId} ja esta {Status} sem registro de webhook",
                pagamentoId, existente.Status);
            return ResultadoNotificacao.Ignorado;
        }

        // 4. Desfecho.
        Pagamento pagamento;
        var pagamentoNovo = existente is null;
        Orcamento? orcamentoAlterado = null;
        string eventoTipo;
        string? motivo = null;
        var estornoAutomatico = false;

        if (pg.Status == StatusPagamentoGateway.Aprovado)
        {
            pagamento = AprovarOuRegistrar(existente, pg, ordemServicoId, agora);
            if (orcamento.Status == StatusOrcamento.Aprovado && pg.Valor == orcamento.ValorTotal)
            {
                eventoTipo = EventoConfirmado;
            }
            else
            {
                // Pagamento que nao pode ser aceito: estorna (chave idempotente) e nunca confirma.
                var estorno = await gateway.EstornarAsync(pagamentoId, pg.Valor, $"estorno-{pagamentoId}", ct);
                pagamento.Estornar(estorno.EstornoId, agora);
                estornoAutomatico = true;
                if (orcamento.Status == StatusOrcamento.Aprovado)
                {
                    orcamento.Cancelar(MotivoValorDivergente, agora);
                    orcamentoAlterado = orcamento;
                    eventoTipo = EventoRecusado;
                    motivo = MotivoValorDivergente;
                }
                else
                {
                    eventoTipo = EventoNenhum;
                }
            }
        }
        else // Recusado
        {
            pagamento = RecusarOuRegistrar(existente, pg, ordemServicoId, agora);
            if (orcamento.Status == StatusOrcamento.Aprovado)
            {
                motivo = pg.DetalheStatus ?? MotivoRecusaPadrao;
                orcamento.Cancelar(motivo, agora);
                orcamentoAlterado = orcamento;
                eventoTipo = EventoRecusado;
            }
            else
            {
                eventoTipo = EventoNenhum;
            }
        }

        // 5. Grava pagamento + registro de webhook (+ orcamento) atomicamente.
        var webhook = new RegistroWebhook(pagamentoId, ordemServicoId, eventoTipo, motivo, EventoPublicado: false, agora);
        if (!await repositorio.RegistrarDesfechoAsync(
                new DesfechoPagamento(webhook, pagamento, pagamentoNovo, orcamentoAlterado), ct))
        {
            // Outra replica registrou primeiro.
            var vencedor = await repositorio.ObterRegistroWebhookAsync(pagamentoId, ct);
            return vencedor is null
                ? ResultadoNotificacao.Duplicado
                : await TratarDuplicadoAsync(vencedor, pg, await ObterPagamentoSalvoAsync(ordemServicoId, pagamentoId, ct), ct);
        }

        // 6. Publica e marca.
        await PublicarEMarcarAsync(eventoTipo, ordemServicoId, pagamentoId, pagamento.Valor, pagamento.Meio, motivo, ct);

        if (estornoAutomatico)
            telemetria.Registrar(new EventoPagamento(ordemServicoId, orcamento.NumeroOs, "EstornoAutomatico", "Sucesso",
                pagamento.Valor, pagamento.Meio, gateway.Nome));
        if (eventoTipo != EventoNenhum)
            telemetria.Registrar(new EventoPagamento(ordemServicoId, orcamento.NumeroOs, eventoTipo,
                eventoTipo == EventoConfirmado ? "Sucesso" : "Falha", pagamento.Valor, pagamento.Meio, gateway.Nome));

        return ResultadoNotificacao.Processado;
    }

    private async Task<Pagamento?> ObterPagamentoSalvoAsync(Guid ordemServicoId, string pagamentoId,
        CancellationToken ct) =>
        (await repositorio.ListarPagamentosAsync(ordemServicoId, ct)).FirstOrDefault(p => p.Id == pagamentoId);

    private static Pagamento AprovarOuRegistrar(Pagamento? existente, PagamentoGateway pg, Guid ordemServicoId,
        DateTime agora)
    {
        if (existente is null)
            return Pagamento.Registrar(pg.PagamentoId, ordemServicoId, pg.Valor, pg.Meio, StatusPagamento.Aprovado,
                pg.DetalheStatus, agora);
        existente.Aprovar(agora);
        return existente;
    }

    private static Pagamento RecusarOuRegistrar(Pagamento? existente, PagamentoGateway pg, Guid ordemServicoId,
        DateTime agora)
    {
        if (existente is null)
            return Pagamento.Registrar(pg.PagamentoId, ordemServicoId, pg.Valor, pg.Meio, StatusPagamento.Recusado,
                pg.DetalheStatus, agora);
        existente.Recusar(pg.DetalheStatus, agora);
        return existente;
    }

    private async Task<ResultadoNotificacao> TratarDuplicadoAsync(RegistroWebhook registro, PagamentoGateway pg,
        Pagamento? salvo, CancellationToken ct)
    {
        if (!registro.EventoPublicado)
        {
            await PublicarEMarcarAsync(registro.EventoTipo, registro.OrdemServicoId, registro.PagamentoId,
                salvo?.Valor ?? pg.Valor, salvo?.Meio ?? pg.Meio, registro.Motivo, ct);
        }
        return ResultadoNotificacao.Duplicado;
    }

    private async Task PublicarEMarcarAsync(string eventoTipo, Guid ordemServicoId, string pagamentoId, decimal valor,
        string meio, string? motivo, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        switch (eventoTipo)
        {
            case EventoConfirmado:
                await publicador.PublicarAsync(new PagamentoConfirmado
                {
                    OrdemServicoId = ordemServicoId,
                    OcorridoEm = agora,
                    PagamentoId = pagamentoId,
                    Valor = valor,
                    Meio = meio,
                }, ct);
                break;
            case EventoRecusado:
                await publicador.PublicarAsync(new PagamentoRecusado
                {
                    OrdemServicoId = ordemServicoId,
                    OcorridoEm = agora,
                    PagamentoId = pagamentoId,
                    Motivo = motivo ?? MotivoRecusaPadrao,
                }, ct);
                break;
        }
        await repositorio.MarcarEventoPublicadoAsync(pagamentoId, ct);
    }
}
