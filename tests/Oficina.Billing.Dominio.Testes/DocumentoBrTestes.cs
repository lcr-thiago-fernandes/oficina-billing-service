using Oficina.Billing.Dominio.Comum;

namespace Oficina.Billing.Dominio.Testes;

public class DocumentoBrTestes
{
    [Theory]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData(null, "")]
    public void DocumentoBr_Normalizar(string? entrada, string esperado) =>
        DocumentoBr.Normalizar(entrada).Should().Be(esperado);
}
