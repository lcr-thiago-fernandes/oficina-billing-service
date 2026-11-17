using Oficina.Billing.Aplicacao.Mensagens;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;
using Oficina.Billing.Dominio.Comum;
using Oficina.Billing.Dominio.Orcamentos;

namespace Oficina.Billing.Aplicacao.CasosDeUso;

public class GerarOrcamentoCasoDeUso(IRepositorioBilling repositorio, IPublicadorMensagens publicador,
    ITelemetriaPagamento telemetria, TimeProvider relogio)
{
    private const string Gateway = "-";

    public async Task ExecutarAsync(GerarOrcamento comando, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;

        Orcamento orcamento;
        try
        {
            var itens = (comando.Itens ?? []).Select(i => new ItemOrcamento(
                i.Tipo == TipoItemOrcamento.Peca ? TipoItem.Peca : TipoItem.Servico,
                i.ReferenciaId, i.Descricao, i.Quantidade, i.PrecoUnitario));
            orcamento = Orcamento.Gerar(comando.OrdemServicoId, comando.NumeroOs,
                new DadosCliente(comando.ClienteDocumento, comando.ClienteNome, comando.ClienteEmail), itens, agora);
        }
        catch (OrcamentoInvalidoException ex)
        {
            await publicador.PublicarAsync(new OrcamentoFalhou
            {
                OrdemServicoId = comando.OrdemServicoId, OcorridoEm = agora, Motivo = ex.Motivo,
            }, ct);
            telemetria.Registrar(new EventoPagamento(comando.OrdemServicoId, comando.NumeroOs, "OrcamentoFalhou",
                "Falha", null, null, Gateway));
            return;
        }

        if (!await repositorio.InserirOrcamentoAsync(orcamento, ct))
        {
            // Mensagem duplicada: republica o desfecho do orcamento ja existente (a queda pode ter ocorrido apos gravar).
            var existente = await repositorio.ObterOrcamentoAsync(comando.OrdemServicoId, ct);
            if (existente is null || existente.Status != StatusOrcamento.Gerado)
                return;
            orcamento = existente;
        }

        await publicador.PublicarAsync(new OrcamentoGerado
        {
            OrdemServicoId = orcamento.OrdemServicoId, OcorridoEm = agora, OrcamentoId = orcamento.Id,
            ValorTotal = orcamento.ValorTotal,
        }, ct);
        telemetria.Registrar(new EventoPagamento(orcamento.OrdemServicoId, orcamento.NumeroOs, "OrcamentoGerado",
            "Sucesso", orcamento.ValorTotal, null, Gateway));
    }
}
