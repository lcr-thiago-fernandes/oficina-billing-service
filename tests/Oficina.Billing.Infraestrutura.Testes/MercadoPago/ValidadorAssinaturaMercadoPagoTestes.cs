using Oficina.Billing.Infraestrutura.MercadoPago;

namespace Oficina.Billing.Infraestrutura.Testes.MercadoPago;

public class ValidadorAssinaturaMercadoPagoTestes
{
    private const string Segredo = "segredo-teste";
    private const string RequestId = "bb56a2f1-6aae-46ac-982e-9dcd3581d08e";
    private const string Ts = "1742505638683";

    private static string Header(string v1) => $"ts={Ts},v1={v1}";

    [Fact]
    public void Valida_AssinaturaCorreta() =>
        ValidadorAssinaturaMercadoPago.Validar(
            Header("b929b54eae1ecf3706b9a6125a7dd57a487da721db504950ec3ece9d05d62a75"), RequestId, "123456", Segredo)
            .Should().BeTrue();

    [Fact]
    public void Valida_DataIdAlfanumericoMaiusculo_UsaMinusculo() =>
        ValidadorAssinaturaMercadoPago.Validar(
            Header("e6f172fe5f500b0a3b5fe5d6b570158899a996b99c130477a9a9fcfe06e8f981"), RequestId, "ABC123DEF", Segredo)
            .Should().BeTrue();

    [Fact]
    public void Valida_SemRequestId_RemoveParDoManifesto() =>
        ValidadorAssinaturaMercadoPago.Validar(
            Header("b26e7a956cc5b56f847df3f3f4657a66bf80421090f87752874208a427328adc"), null, "123456", Segredo)
            .Should().BeTrue();

    [Fact]
    public void Rejeita_HashErrado() =>
        ValidadorAssinaturaMercadoPago.Validar(Header(new string('0', 64)), RequestId, "123456", Segredo)
            .Should().BeFalse();

    [Fact]
    public void Rejeita_SegredoErrado() =>
        ValidadorAssinaturaMercadoPago.Validar(
            Header("b929b54eae1ecf3706b9a6125a7dd57a487da721db504950ec3ece9d05d62a75"), RequestId, "123456", "outro")
            .Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("ts=1742505638683")]
    [InlineData("v1=abc")]
    public void Rejeita_HeaderMalformado_SemExcecao(string? header) =>
        ValidadorAssinaturaMercadoPago.Validar(header, RequestId, "123456", Segredo).Should().BeFalse();

    [Fact]
    public void Rejeita_SegredoVazio() =>
        ValidadorAssinaturaMercadoPago.Validar(
            Header("b929b54eae1ecf3706b9a6125a7dd57a487da721db504950ec3ece9d05d62a75"), RequestId, "123456", "")
            .Should().BeFalse();

    [Fact]
    public void Rejeita_HexInvalido_SemExcecao() =>
        ValidadorAssinaturaMercadoPago.Validar(Header("zz"), RequestId, "123456", Segredo).Should().BeFalse();
}
