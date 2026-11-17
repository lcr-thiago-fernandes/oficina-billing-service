using Oficina.Billing.Aplicacao.CasosDeUso;

namespace Oficina.Billing.Aplicacao.Testes;

public class SolicitanteTestes
{
    [Fact]
    public void PodeAcessar_ClienteComCpfFormatado_True() =>
        new Solicitante("Cliente", "529.982.247-25").PodeAcessar("52998224725").Should().BeTrue();

    [Fact]
    public void PodeAcessar_ClienteDeOutroDocumento_False() =>
        new Solicitante("Cliente", "111.444.777-35").PodeAcessar("52998224725").Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void PodeAcessar_ClienteSemDocumento_False(string? doc) =>
        new Solicitante("Cliente", doc).PodeAcessar("").Should().BeFalse();

    [Theory]
    [InlineData("Atendente")]
    [InlineData("Admin")]
    public void PodeAcessar_Atendente_True(string perfil) =>
        new Solicitante(perfil, null).PodeAcessar("52998224725").Should().BeTrue();

    [Fact]
    public void PodeAcessar_PerfilDesconhecido_False() =>
        new Solicitante("Visitante", "52998224725").PodeAcessar("52998224725").Should().BeFalse();
}
