using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MassTransit;
using Oficina.Billing.Aplicacao.Mensagens;

namespace Oficina.Billing.Aplicacao.Testes.Contratos;

public class ContratoMensagensTestes
{
    public static TheoryData<Type> TiposDoBilling => new(ContratosSaga.Tipos);

    [Fact]
    public void ContratosSaga_TemOsOnzeTiposDoBilling()
    {
        ContratosSaga.Tipos.Select(ContratosSaga.Nome).Should().BeEquivalentTo(new[]
        {
            "GerarOrcamento", "OrcamentoGerado", "OrcamentoFalhou", "OrcamentoAprovado", "OrcamentoRejeitado",
            "PagamentoConfirmado", "PagamentoRecusado", "EstornarPagamento", "PagamentoEstornado", "EstornoFalhou",
            "CancelarOrcamento"
        });
    }

    [Theory]
    [MemberData(nameof(TiposDoBilling))]
    public void Json_RoundTrip_IdenticoAoExemplo(Type tipo)
    {
        var nome = ContratosSaga.Nome(tipo);
        var original = JsonNode.Parse(File.ReadAllText($"Contratos/{nome}.json"))!;
        var objeto = JsonSerializer.Deserialize(original.ToJsonString(), tipo, OpcoesJsonContrato.Criar());
        var reserializado = JsonSerializer.SerializeToNode(objeto, tipo, OpcoesJsonContrato.Criar());

        IguaisPorValor(original, reserializado).Should().BeTrue($"{nome} divergiu: {reserializado}");
    }

    [Theory]
    [MemberData(nameof(TiposDoBilling))]
    public void MessageUrn_SegueOContrato(Type tipo)
    {
        tipo.GetCustomAttribute<MessageUrnAttribute>()!.Urn.ToString()
            .Should().Be($"urn:message:oficina:saga:{ContratosSaga.Nome(tipo)}");
    }

    [Theory]
    [MemberData(nameof(TiposDoBilling))]
    public void EntityName_SegueOContrato(Type tipo)
    {
        tipo.GetCustomAttribute<EntityNameAttribute>()!.EntityName
            .Should().Be($"oficina-saga-{ContratosSaga.Nome(tipo)}");
    }

    [Fact]
    public void IMensagemSaga_ExcluidaDaTopologia()
    {
        typeof(IMensagemSaga).GetCustomAttribute<ExcludeFromTopologyAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void Arquivos_BatemComSHA256SUMS()
    {
        var linhas = File.ReadAllLines("Contratos/SHA256SUMS").Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        linhas.Should().HaveCount(16);
        foreach (var linha in linhas)
        {
            var partes = linha.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var hash = partes[0];
            var arquivo = partes[1].Trim().TrimStart('*');
            var calculado = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes($"Contratos/{arquivo}"))).ToLowerInvariant();
            calculado.Should().Be(hash, $"{arquivo} foi alterado");
        }
    }

    [Fact]
    public void GerarOrcamento_TipoDoItem_SerializaComoString()
    {
        var mensagem = new GerarOrcamento
        {
            OrdemServicoId = Guid.NewGuid(),
            OcorridoEm = DateTime.UtcNow,
            NumeroOs = 1,
            ClienteDocumento = "52998224725",
            ClienteNome = "Maria",
            Itens = new[]
            {
                new ItemGerarOrcamento { Tipo = TipoItemOrcamento.Peca, ReferenciaId = Guid.NewGuid(), Descricao = "Filtro", Quantidade = 1, PrecoUnitario = 10m }
            }
        };

        var json = JsonSerializer.SerializeToNode(mensagem, OpcoesJsonContrato.Criar())!;

        json["itens"]![0]!["tipo"]!.GetValue<string>().Should().Be("Peca");
    }

    // DeepEquals exige a mesma representacao numerica (120.00 vs 120); aqui numeros comparam por valor decimal.
    private static bool IguaisPorValor(JsonNode? a, JsonNode? b)
    {
        switch (a, b)
        {
            case (null, null):
                return true;
            case (JsonObject oa, JsonObject ob):
                return oa.Count == ob.Count && oa.All(p => ob.ContainsKey(p.Key) && IguaisPorValor(p.Value, ob[p.Key]));
            case (JsonArray aa, JsonArray ab):
                return aa.Count == ab.Count && aa.Zip(ab).All(par => IguaisPorValor(par.First, par.Second));
            case (JsonValue va, JsonValue vb):
                if (va.TryGetValue<decimal>(out var da) && vb.TryGetValue<decimal>(out var db)) return da == db;
                return JsonNode.DeepEquals(va, vb);
            default:
                return false;
        }
    }
}
