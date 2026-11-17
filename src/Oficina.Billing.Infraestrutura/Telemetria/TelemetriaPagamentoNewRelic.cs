using Microsoft.Extensions.Logging;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Aplicacao.Telemetria;

namespace Oficina.Billing.Infraestrutura.Telemetria;

/// <summary>Evento customizado <c>PagamentoEvento</c> no New Relic. Sem agente carregado a API e no-op; telemetria nunca derruba o fluxo.</summary>
public sealed class TelemetriaPagamentoNewRelic(ILogger<TelemetriaPagamentoNewRelic> logger) : ITelemetriaPagamento
{
    public const string NomeEvento = "PagamentoEvento";

    public void Registrar(EventoPagamento evento)
    {
        try
        {
            NewRelic.Api.Agent.NewRelic.RecordCustomEvent(NomeEvento, MontarAtributos(evento));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao registrar {Evento} no New Relic para a OS {OrdemServicoId}; seguindo sem telemetria",
                NomeEvento, evento.OrdemServicoId);
        }
    }

    public static Dictionary<string, object> MontarAtributos(EventoPagamento evento)
    {
        var atributos = new Dictionary<string, object>
        {
            ["ordemServicoId"] = evento.OrdemServicoId.ToString(),
            ["numeroOs"] = evento.NumeroOs,
            ["etapa"] = evento.Etapa,
            ["resultado"] = evento.Resultado,
            ["gateway"] = evento.Gateway,
        };
        if (evento.Valor is { } valor)
            atributos["valor"] = valor;
        if (!string.IsNullOrEmpty(evento.Meio))
            atributos["meio"] = evento.Meio;
        return atributos;
    }
}
