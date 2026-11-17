namespace Oficina.Billing.Infraestrutura.Simulador;

public enum ModoPagamento { MercadoPago, Simulado }

public class OpcoesPagamento
{
    public const string Secao = "Pagamento";

    public ModoPagamento Modo { get; set; } = ModoPagamento.Simulado;

    /// <summary>true faz todo estorno do gateway simulado falhar como indisponibilidade (demonstracao de compensacao).</summary>
    public bool SimularFalhaEstorno { get; set; }
}
