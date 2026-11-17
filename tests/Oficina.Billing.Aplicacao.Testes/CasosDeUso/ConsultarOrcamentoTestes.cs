using Moq;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Portas;

namespace Oficina.Billing.Aplicacao.Testes.CasosDeUso;

public class ConsultarOrcamentoTestes
{
    private readonly Mock<IRepositorioBilling> _repo = new();

    [Fact]
    public async Task Consultar_ClienteDono_RetornaResposta()
    {
        var o = Cenario.Orcamento();
        _repo.Setup(r => r.ObterOrcamentoAsync(Cenario.OsId, It.IsAny<CancellationToken>())).ReturnsAsync(o);

        var r = await new ConsultarOrcamentoCasoDeUso(_repo.Object).ExecutarAsync(Cenario.OsId, Cenario.Dono, default);

        r.OrcamentoId.Should().Be(o.Id);
        r.Status.Should().Be("Gerado");
        r.ValorTotal.Should().Be(191.00m);
        r.Itens.Should().HaveCount(2);
        r.Itens[1].Should().Be(new ItemOrcamentoResposta("Peca", "Filtro", 2, 50.00m, 100.00m));
    }

    [Fact]
    public async Task Consultar_ClienteDeOutroDocumento_LancaNaoEncontrado()
    {
        _repo.Setup(r => r.ObterOrcamentoAsync(Cenario.OsId, It.IsAny<CancellationToken>())).ReturnsAsync(Cenario.Orcamento());

        var act = () => new ConsultarOrcamentoCasoDeUso(_repo.Object).ExecutarAsync(Cenario.OsId, Cenario.Outro, default);

        await act.Should().ThrowAsync<OrcamentoNaoEncontradoException>();
    }

    [Fact]
    public async Task Consultar_Inexistente_LancaNaoEncontrado()
    {
        var act = () => new ConsultarOrcamentoCasoDeUso(_repo.Object).ExecutarAsync(Cenario.OsId, Cenario.Atendente, default);

        await act.Should().ThrowAsync<OrcamentoNaoEncontradoException>();
    }
}
