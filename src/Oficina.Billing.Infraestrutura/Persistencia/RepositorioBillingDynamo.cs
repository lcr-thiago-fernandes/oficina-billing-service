using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Infraestrutura.Persistencia;

/// <summary>
/// Tabela unica do Billing. Toda escrita e condicional: insercao com <c>attribute_not_exists(pk)</c>, atualizacao
/// com <c>versao = :v</c> (otimista). O desfecho de um webhook grava registro de dedupe + pagamento (+ orcamento)
/// numa unica transacao.
/// </summary>
public class RepositorioBillingDynamo(IAmazonDynamoDB cliente, IOptions<OpcoesDynamo> opcoes, TimeProvider relogio)
    : IRepositorioBilling
{
    private const string CondicaoNovo = "attribute_not_exists(pk)";
    private const string CondicaoVersao = "versao = :v";
    private const string CodigoCondicaoFalhou = "ConditionalCheckFailed";
    private const string CodigoConflitoTransacao = "TransactionConflict";
    private static readonly TimeSpan RetencaoWebhook = TimeSpan.FromDays(90);

    private readonly string _tabela = opcoes.Value.NomeTabela;

    public async Task<bool> InserirOrcamentoAsync(Orcamento orcamento, CancellationToken ct)
    {
        try
        {
            await cliente.PutItemAsync(new PutItemRequest
            {
                TableName = _tabela,
                Item = MapeadorItens.ParaItem(orcamento),
                ConditionExpression = CondicaoNovo,
            }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<Orcamento?> ObterOrcamentoAsync(Guid ordemServicoId, CancellationToken ct)
    {
        var item = await ObterItemAsync(MapeadorItens.PkOs(ordemServicoId), MapeadorItens.SkOrcamento, ct);
        return item is null ? null : MapeadorItens.DeItemOrcamento(item);
    }

    public async Task AtualizarOrcamentoAsync(Orcamento orcamento, CancellationToken ct)
    {
        try
        {
            await cliente.PutItemAsync(new PutItemRequest
            {
                TableName = _tabela,
                Item = MapeadorItens.ParaItem(orcamento, orcamento.Versao + 1),
                ConditionExpression = CondicaoVersao,
                ExpressionAttributeValues = ValoresVersao(orcamento.Versao),
            }, ct);
        }
        catch (ConditionalCheckFailedException ex)
        {
            throw new ConcorrenciaException(
                $"Orcamento da OS {orcamento.OrdemServicoId} foi alterado por outra operacao (versao {orcamento.Versao})", ex);
        }
        orcamento.IncrementarVersao();
    }

    public async Task<IReadOnlyList<Pagamento>> ListarPagamentosAsync(Guid ordemServicoId, CancellationToken ct)
    {
        var pagamentos = new List<Pagamento>();
        Dictionary<string, AttributeValue>? inicio = null;
        do
        {
            var resposta = await cliente.QueryAsync(new QueryRequest
            {
                TableName = _tabela,
                KeyConditionExpression = "pk = :pk AND begins_with(sk, :prefixo)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new() { S = MapeadorItens.PkOs(ordemServicoId) },
                    [":prefixo"] = new() { S = MapeadorItens.PrefixoSkPagamento },
                },
                ConsistentRead = true,
                ExclusiveStartKey = inicio,
            }, ct);
            pagamentos.AddRange((resposta.Items ?? []).Select(MapeadorItens.DeItemPagamento));
            inicio = resposta.LastEvaluatedKey is { Count: > 0 } ? resposta.LastEvaluatedKey : null;
        } while (inicio is not null);
        return pagamentos;
    }

    public async Task SalvarPagamentoAsync(Pagamento pagamento, bool novo, CancellationToken ct)
    {
        try
        {
            await cliente.PutItemAsync(new PutItemRequest
            {
                TableName = _tabela,
                Item = MapeadorItens.ParaItem(pagamento, novo ? pagamento.Versao : pagamento.Versao + 1),
                ConditionExpression = novo ? CondicaoNovo : CondicaoVersao,
                ExpressionAttributeValues = novo ? null : ValoresVersao(pagamento.Versao),
            }, ct);
        }
        catch (ConditionalCheckFailedException ex)
        {
            throw new ConcorrenciaException(novo
                ? $"Pagamento {pagamento.Id} ja existe"
                : $"Pagamento {pagamento.Id} foi alterado por outra operacao (versao {pagamento.Versao})", ex);
        }
        if (!novo)
            pagamento.IncrementarVersao();
    }

    public async Task<RegistroWebhook?> ObterRegistroWebhookAsync(string pagamentoId, CancellationToken ct)
    {
        var item = await ObterItemAsync(MapeadorItens.PkWebhook(pagamentoId), MapeadorItens.SkWebhook, ct);
        return item is null ? null : MapeadorItens.DeItemWebhook(item);
    }

    public async Task<bool> RegistrarDesfechoAsync(DesfechoPagamento desfecho, CancellationToken ct)
    {
        var (webhook, pagamento, pagamentoNovo, orcamento) = desfecho;
        var expiraEm = relogio.GetUtcNow() + RetencaoWebhook;

        // A ordem importa: o motivo de cancelamento de indice 0 (webhook) distingue duplicata de conflito.
        var itens = new List<TransactWriteItem>
        {
            new()
            {
                Put = new Put
                {
                    TableName = _tabela,
                    Item = MapeadorItens.ParaItem(webhook, expiraEm),
                    ConditionExpression = CondicaoNovo,
                },
            },
            new()
            {
                Put = new Put
                {
                    TableName = _tabela,
                    Item = MapeadorItens.ParaItem(pagamento, pagamentoNovo ? pagamento.Versao : pagamento.Versao + 1),
                    ConditionExpression = pagamentoNovo ? CondicaoNovo : CondicaoVersao,
                    ExpressionAttributeValues = pagamentoNovo ? null : ValoresVersao(pagamento.Versao),
                },
            },
        };
        if (orcamento is not null)
        {
            itens.Add(new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = _tabela,
                    Item = MapeadorItens.ParaItem(orcamento, orcamento.Versao + 1),
                    ConditionExpression = CondicaoVersao,
                    ExpressionAttributeValues = ValoresVersao(orcamento.Versao),
                },
            });
        }

        try
        {
            await cliente.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = itens }, ct);
        }
        catch (TransactionCanceledException ex)
        {
            var motivos = ex.CancellationReasons ?? [];
            if (motivos.Count > 0 && motivos[0].Code == CodigoCondicaoFalhou)
                return false;
            if (motivos.Any(m => m.Code is CodigoCondicaoFalhou or CodigoConflitoTransacao))
                throw new ConcorrenciaException(
                    $"Desfecho do pagamento {pagamento.Id} (OS {pagamento.OrdemServicoId}) conflitou com outra operacao", ex);
            throw;
        }

        if (!pagamentoNovo)
            pagamento.IncrementarVersao();
        orcamento?.IncrementarVersao();
        return true;
    }

    public async Task MarcarEventoPublicadoAsync(string pagamentoId, CancellationToken ct)
    {
        try
        {
            await cliente.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _tabela,
                Key = MapeadorItens.Chave(MapeadorItens.PkWebhook(pagamentoId), MapeadorItens.SkWebhook),
                UpdateExpression = "SET eventoPublicado = :sim",
                ConditionExpression = "attribute_exists(pk)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":sim"] = new() { BOOL = true },
                },
            }, ct);
        }
        catch (ConditionalCheckFailedException ex)
        {
            // Sem a condicao o UpdateItem criaria um registro orfao (sem TTL nem dados do desfecho).
            throw new InvalidOperationException($"Registro de webhook do pagamento {pagamentoId} nao existe", ex);
        }
    }

    private async Task<Dictionary<string, AttributeValue>?> ObterItemAsync(string pk, string sk, CancellationToken ct)
    {
        var resposta = await cliente.GetItemAsync(new GetItemRequest
        {
            TableName = _tabela,
            Key = MapeadorItens.Chave(pk, sk),
            ConsistentRead = true,
        }, ct);
        return resposta.Item is { Count: > 0 } ? resposta.Item : null;
    }

    private static Dictionary<string, AttributeValue> ValoresVersao(int versao) => new()
    {
        [":v"] = new AttributeValue { N = versao.ToString(CultureInfo.InvariantCulture) },
    };
}
