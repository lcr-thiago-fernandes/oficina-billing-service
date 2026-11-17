using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using Oficina.Billing.Aplicacao.Portas;

namespace Oficina.Billing.Infraestrutura.Simulador;

/// <summary>
/// Gateway simulado para testes, ambiente local e demonstracao (<c>Pagamento:Modo = Simulado</c>).
/// </summary>
/// <remarks>
/// Sem estado compartilhado entre pods: o id <c>sim-{a|r}-{osId:N}-{valor}-{nonce:N}</c> carrega o resultado, a OS e o
/// valor, entao qualquer replica (ou a mesma apos um redeploy) consulta e estorna um pagamento emitido por outra.
/// O nonce (Guid) garante ids unicos entre pods/redeploys, sem colidir com os registros <c>WEBHOOK#</c> de 90 dias.
/// A memoria local guarda apenas o status <c>Estornado</c> como sobreposicao (consulta apos estorno no mesmo pod).
/// </remarks>
public class GatewayPagamentoSimulado(IOptions<OpcoesPagamento> opcoes) : IGatewayPagamento
{
    public const string DetalheRecusado = "cc_rejected_simulado";
    public const string Meio = "simulado";
    private const string Prefixo = "sim";
    private const string MarcaAprovado = "a";
    private const string MarcaRecusado = "r";

    private readonly ConcurrentDictionary<string, PagamentoGateway> _estornados = new();

    public string Nome => "Simulado";

    public Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken ct) =>
        Task.FromResult(new CobrancaCriada($"sim-pref-{cobranca.OrdemServicoId}",
            $"https://simulador.local/pagamentos/{cobranca.OrdemServicoId}"));

    /// <summary>Gera o id (autodescritivo e unico) de um pagamento ja decidido, a ser notificado.</summary>
    public string RegistrarPagamento(Guid osId, decimal valor, bool aprovado) =>
        string.Join('-', Prefixo, aprovado ? MarcaAprovado : MarcaRecusado, osId.ToString("N"),
            valor.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));

    public Task<PagamentoGateway?> ObterPagamentoAsync(string pagamentoId, CancellationToken ct) =>
        Task.FromResult(_estornados.TryGetValue(pagamentoId, out var estornado) ? estornado : Decodificar(pagamentoId));

    public Task<EstornoGateway> EstornarAsync(string pagamentoId, decimal valor, string chaveIdempotencia,
        CancellationToken ct)
    {
        if (opcoes.Value.SimularFalhaEstorno)
            throw new GatewayPagamentoIndisponivelException("Falha de estorno simulada (Pagamento:SimularFalhaEstorno)");

        var pagamento = Decodificar(pagamentoId)
            ?? throw new InvalidOperationException($"Pagamento simulado {pagamentoId} invalido");
        _estornados[pagamentoId] = pagamento with { Status = StatusPagamentoGateway.Estornado };
        return Task.FromResult(new EstornoGateway($"sim-estorno-{pagamentoId}", valor));
    }

    private static PagamentoGateway? Decodificar(string pagamentoId)
    {
        var partes = pagamentoId.Split('-');
        if (partes.Length != 5 || partes[0] != Prefixo
            || (partes[1] != MarcaAprovado && partes[1] != MarcaRecusado)
            || !Guid.TryParseExact(partes[2], "N", out var osId)
            || !decimal.TryParse(partes[3], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor)
            || !Guid.TryParseExact(partes[4], "N", out _))
            return null;

        var aprovado = partes[1] == MarcaAprovado;
        return new PagamentoGateway(pagamentoId, osId,
            aprovado ? StatusPagamentoGateway.Aprovado : StatusPagamentoGateway.Recusado,
            aprovado ? null : DetalheRecusado, valor, Meio);
    }
}
