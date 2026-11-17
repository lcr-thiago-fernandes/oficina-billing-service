using System.Globalization;
using Amazon.DynamoDBv2.Model;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Dominio.Orcamentos;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Infraestrutura.Persistencia;

/// <summary>
/// Conversao entre agregados e itens da tabela unica (<c>pk</c>/<c>sk</c>). Decimais em <c>N</c> com cultura
/// invariante, datas ISO-8601 (<c>O</c>), opcionais nulos nao sao gravados. No SDK v4 colecoes de
/// <see cref="AttributeValue"/> ausentes vem <c>null</c>.
/// </summary>
public static class MapeadorItens
{
    public const string SkOrcamento = "ORCAMENTO";
    public const string PrefixoSkPagamento = "PAGAMENTO#";
    public const string SkWebhook = "WEBHOOK";
    private const string PrefixoPkOs = "OS#";
    private const string PrefixoPkWebhook = "WEBHOOK#";

    public static string PkOs(Guid ordemServicoId) => PrefixoPkOs + ordemServicoId;

    public static string SkPagamento(string pagamentoId) => PrefixoSkPagamento + pagamentoId;

    public static string PkWebhook(string pagamentoId) => PrefixoPkWebhook + pagamentoId;

    public static Dictionary<string, AttributeValue> Chave(string pk, string sk) => new()
    {
        ["pk"] = S(pk),
        ["sk"] = S(sk),
    };

    /// <param name="versao">Versao gravada; padrao <c>o.Versao</c>.</param>
    public static Dictionary<string, AttributeValue> ParaItem(Orcamento o, int? versao = null)
    {
        var item = Chave(PkOs(o.OrdemServicoId), SkOrcamento);
        item["orcamentoId"] = S(o.Id.ToString());
        item["ordemServicoId"] = S(o.OrdemServicoId.ToString());
        item["numeroOs"] = N(o.NumeroOs);
        item["clienteDocumento"] = S(o.Cliente.Documento);
        item["clienteNome"] = S(o.Cliente.Nome);
        Opcional(item, "clienteEmail", o.Cliente.Email);
        item["itens"] = new AttributeValue
        {
            L = o.Itens.Select(i => new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["tipo"] = S(i.Tipo.ToString()),
                    ["referenciaId"] = S(i.ReferenciaId.ToString()),
                    ["descricao"] = S(i.Descricao),
                    ["quantidade"] = N(i.Quantidade),
                    ["precoUnitario"] = N(i.PrecoUnitario),
                },
            }).ToList(),
        };
        item["valorTotal"] = N(o.ValorTotal);
        item["status"] = S(o.Status.ToString());
        Opcional(item, "motivo", o.Motivo);
        Opcional(item, "preferenciaId", o.PreferenciaId);
        Opcional(item, "linkPagamento", o.LinkPagamento);
        item["criadoEm"] = Data(o.CriadoEm);
        item["atualizadoEm"] = Data(o.AtualizadoEm);
        item["versao"] = N(versao ?? o.Versao);
        return item;
    }

    public static Orcamento DeItemOrcamento(Dictionary<string, AttributeValue> item)
    {
        var itens = (item["itens"].L ?? new List<AttributeValue>())
            .Select(a =>
            {
                var m = a.M ?? new Dictionary<string, AttributeValue>();
                return new ItemOrcamento(Enum.Parse<TipoItem>(m["tipo"].S), Guid.Parse(m["referenciaId"].S),
                    m["descricao"].S, LerInt(m["quantidade"]), LerDecimal(m["precoUnitario"]));
            })
            .ToList();

        return Orcamento.Reconstituir(
            Guid.Parse(item["orcamentoId"].S),
            Guid.Parse(item["ordemServicoId"].S),
            LerInt(item["numeroOs"]),
            new DadosCliente(item["clienteDocumento"].S, item["clienteNome"].S, LerOpcional(item, "clienteEmail")),
            itens.AsReadOnly(),
            LerDecimal(item["valorTotal"]),
            Enum.Parse<StatusOrcamento>(item["status"].S),
            LerOpcional(item, "motivo"),
            LerOpcional(item, "preferenciaId"),
            LerOpcional(item, "linkPagamento"),
            LerData(item["criadoEm"]),
            LerData(item["atualizadoEm"]),
            LerInt(item["versao"]));
    }

    /// <param name="versao">Versao gravada; padrao <c>p.Versao</c>.</param>
    public static Dictionary<string, AttributeValue> ParaItem(Pagamento p, int? versao = null)
    {
        var item = Chave(PkOs(p.OrdemServicoId), SkPagamento(p.Id));
        item["pagamentoId"] = S(p.Id);
        item["valor"] = N(p.Valor);
        item["meio"] = S(p.Meio);
        item["status"] = S(p.Status.ToString());
        Opcional(item, "detalheStatus", p.DetalheStatus);
        Opcional(item, "estornoId", p.EstornoId);
        item["criadoEm"] = Data(p.CriadoEm);
        item["atualizadoEm"] = Data(p.AtualizadoEm);
        item["versao"] = N(versao ?? p.Versao);
        return item;
    }

    public static Pagamento DeItemPagamento(Dictionary<string, AttributeValue> item) =>
        Pagamento.Reconstituir(
            item["pagamentoId"].S,
            Guid.Parse(item["pk"].S[PrefixoPkOs.Length..]),
            LerDecimal(item["valor"]),
            item["meio"].S,
            Enum.Parse<StatusPagamento>(item["status"].S),
            LerOpcional(item, "detalheStatus"),
            LerOpcional(item, "estornoId"),
            LerData(item["criadoEm"]),
            LerData(item["atualizadoEm"]),
            LerInt(item["versao"]));

    /// <param name="expiraEm">Instante de expiracao (TTL do DynamoDB, epoch em segundos).</param>
    public static Dictionary<string, AttributeValue> ParaItem(RegistroWebhook w, DateTimeOffset expiraEm)
    {
        var item = Chave(PkWebhook(w.PagamentoId), SkWebhook);
        item["ordemServicoId"] = S(w.OrdemServicoId.ToString());
        item["eventoTipo"] = S(w.EventoTipo);
        Opcional(item, "motivo", w.Motivo);
        item["eventoPublicado"] = new AttributeValue { BOOL = w.EventoPublicado };
        item["criadoEm"] = Data(w.CriadoEm);
        item["expiraEm"] = N(expiraEm.ToUnixTimeSeconds());
        return item;
    }

    public static RegistroWebhook DeItemWebhook(Dictionary<string, AttributeValue> item) =>
        new(item["pk"].S[PrefixoPkWebhook.Length..],
            Guid.Parse(item["ordemServicoId"].S),
            item["eventoTipo"].S,
            LerOpcional(item, "motivo"),
            item["eventoPublicado"].BOOL ?? false,
            LerData(item["criadoEm"]));

    private static AttributeValue S(string valor) => new() { S = valor };

    private static AttributeValue N(long valor) => new() { N = valor.ToString(CultureInfo.InvariantCulture) };

    private static AttributeValue N(decimal valor) => new() { N = valor.ToString(CultureInfo.InvariantCulture) };

    private static AttributeValue Data(DateTime valor) => S(valor.ToString("O", CultureInfo.InvariantCulture));

    private static void Opcional(Dictionary<string, AttributeValue> item, string nome, string? valor)
    {
        if (valor is not null)
            item[nome] = S(valor);
    }

    private static string? LerOpcional(Dictionary<string, AttributeValue> item, string nome) =>
        item.TryGetValue(nome, out var valor) ? valor.S : null;

    private static int LerInt(AttributeValue valor) => int.Parse(valor.N, CultureInfo.InvariantCulture);

    private static decimal LerDecimal(AttributeValue valor) => decimal.Parse(valor.N, CultureInfo.InvariantCulture);

    private static DateTime LerData(AttributeValue valor) =>
        DateTime.Parse(valor.S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
