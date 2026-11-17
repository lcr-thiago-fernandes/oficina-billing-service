namespace Oficina.Billing.Dominio.Orcamentos;

/// <param name="Documento">Documento normalizado (somente digitos).</param>
public record DadosCliente(string Documento, string Nome, string? Email);
