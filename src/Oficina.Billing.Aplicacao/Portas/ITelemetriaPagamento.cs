using Oficina.Billing.Aplicacao.Telemetria;

namespace Oficina.Billing.Aplicacao.Portas;

public interface ITelemetriaPagamento
{
    void Registrar(EventoPagamento evento);
}
