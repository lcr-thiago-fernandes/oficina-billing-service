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
    private const string MotivoPagamentoEstornado = "pagamento_estornado";

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

        // 2. Desfecho ja gravado: republica se a publicacao anterior nao foi concluida. Vem antes de qualquer regra
        //    sobre o status do gateway: apos um estorno automatico o MP reentrega o pagamento ja "refunded", e o
        //    evento gravado (ex.: PagamentoRecusado "valor_divergente") ainda precisa chegar a saga.
        var registro = await repositorio.ObterRegistroWebhookAsync(pagamentoId, ct);
        var pagamentos = await repositorio.ListarPagamentosAsync(ordemServicoId, ct);
        var existente = pagamentos.FirstOrDefault(p => p.Id == pagamentoId);
        if (registro is not null)
            return await TratarDuplicadoAsync(registro, pg, existente, ct);

        var agora = relogio.GetUtcNow().UtcDateTime;

        // 3. Pendente: so registra (N3).
        if (pg.Status == StatusPagamentoGateway.Pendente)
        {
            if (existente is null)
            {
                await repositorio.SalvarPagamentoAsync(Pagamento.Registrar(pagamentoId, ordemServicoId, pg.Valor,
                    pg.Meio, StatusPagamento.Pendente, pg.DetalheStatus, agora), novo: true, ct);
            }
            return ResultadoNotificacao.Pendente;
        }

        if (existente is not null && existente.Status != StatusPagamento.Pendente)
        {
            // Ja decidido sem registro de webhook: estorno pela compensacao (que publica o PagamentoEstornado) ou
            // registro expirado pelo TTL. Nada a decidir de novo.
            logger.LogWarning("Notificacao ignorada: pagamento {PagamentoId} ja esta {Status} sem registro de webhook",
                pagamentoId, existente.Status);
            return ResultadoNotificacao.Ignorado;
        }

        // 4. Desfecho. Um pagamento ja aprovado da OS tem precedencia: outro aprovado e estornado, um recusado
        //    nao mexe no orcamento, e nenhum dos dois gera evento para a saga (que ja recebeu o PagamentoConfirmado).
        var outroAprovado = pagamentos.Any(p => p.Id != pagamentoId && p.Status == StatusPagamento.Aprovado);
        var orcamentoAguardaPagamento = orcamento.Status == StatusOrcamento.Aprovado && !outroAprovado;

        Pagamento pagamento;
        var pagamentoNovo = existente is null;
        Orcamento? orcamentoAlterado = null;
        var eventoTipo = EventoNenhum;
        string? motivo = null;
        var estornoAutomatico = false;

        if (pg.Status is StatusPagamentoGateway.Aprovado or StatusPagamentoGateway.Estornado)
        {
            // Estornado sem desfecho gravado = queda entre o estorno automatico e a gravacao, ou devolucao/chargeback
            // feito direto no gateway: refaz a decisao como aprovado, mas nunca confirma.
            pagamento = AprovarOuRegistrar(existente, pg, ordemServicoId, agora);
            if (pg.Status == StatusPagamentoGateway.Aprovado && orcamentoAguardaPagamento &&
                pg.Valor == orcamento.ValorTotal)
            {
                eventoTipo = EventoConfirmado;
            }
            else
            {
                if (pg.Status == StatusPagamentoGateway.Estornado)
                {
                    // O dinheiro ja voltou: um novo POST de estorno (fora da nossa chave) responderia 4xx e
                    // prenderia a notificacao em loop de reentrega.
                    pagamento.Estornar(EstornarPagamentoCasoDeUso.EstornoExterno, agora);
                }
                else
                {
                    // Pagamento que nao pode ser aceito: estorna (chave idempotente) e nunca confirma.
                    var estorno = await gateway.EstornarAsync(pagamentoId, pg.Valor, $"estorno-{pagamentoId}", ct);
                    pagamento.Estornar(estorno.EstornoId, agora);
                    estornoAutomatico = true;
                }
                if (orcamentoAguardaPagamento)
                {
                    motivo = pg.Valor != orcamento.ValorTotal ? MotivoValorDivergente : MotivoPagamentoEstornado;
                    orcamento.Cancelar(motivo, agora);
                    orcamentoAlterado = orcamento;
                    eventoTipo = EventoRecusado;
                }
            }
        }
        else // Recusado
        {
            pagamento = RecusarOuRegistrar(existente, pg, ordemServicoId, agora);
            if (orcamentoAguardaPagamento)
            {
                motivo = pg.DetalheStatus ?? MotivoRecusaPadrao;
                orcamento.Cancelar(motivo, agora);
                orcamentoAlterado = orcamento;
                eventoTipo = EventoRecusado;
            }
        }

        // 5. Grava pagamento + registro de webhook (+ orcamento) atomicamente.
        var webhook = new RegistroWebhook(pagamentoId, ordemServicoId, eventoTipo, motivo, EventoPublicado: false, agora);
        if (!await repositorio.RegistrarDesfechoAsync(
                new DesfechoPagamento(webhook, pagamento, pagamentoNovo, orcamentoAlterado), ct))
        {
            // Outra replica registrou primeiro. Se ela ainda nao publicou, esta tambem publica: o evento e identico
            // (mesmo registro), o que e aceitavel com entrega at-least-once.
            var vencedor = await repositorio.ObterRegistroWebhookAsync(pagamentoId, ct)
                ?? throw new ConcorrenciaException(
                    $"Registro de webhook do pagamento {pagamentoId} ja existe mas nao pode ser lido");
            return await TratarDuplicadoAsync(vencedor, pg, null, ct);
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
