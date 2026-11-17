using Oficina.Billing.Dominio.Comum;

namespace Oficina.Billing.Aplicacao.CasosDeUso;

public record Solicitante(string Perfil, string? Documento)
{
    public bool PodeAcessar(string documentoCliente)
    {
        switch (Perfil)
        {
            case "Atendente":
            case "Admin":
                return true;
            case "Cliente":
                var proprio = DocumentoBr.Normalizar(Documento);
                return proprio.Length > 0 && proprio == documentoCliente;
            default:
                return false;
        }
    }
}
