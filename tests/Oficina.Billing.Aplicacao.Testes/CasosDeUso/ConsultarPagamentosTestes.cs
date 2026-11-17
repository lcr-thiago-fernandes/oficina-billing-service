using Moq;
using Oficina.Billing.Aplicacao.CasosDeUso;
using Oficina.Billing.Aplicacao.Portas;
using Oficina.Billing.Dominio.Pagamentos;

namespace Oficina.Billing.Aplicacao.Testes.CasosDeUso;

public class ConsultarPagamentosTestes
{
    private readonly Mock<IRepositorioBilling> _repo = new();

    private ConsultarPagamentosCasoDeUso Criar() => new(_repo.Object);

    [Fact]
    public async Task ClienteDono_ListaPagamentos()
    {
        _repo.Setup(r => r.ObterOrcamentoAsync(Cenario.OsId, It.IsAny<CancellationToken>())).ReturnsAsync(Cenario.Orcamento());
        var recusado = Pagamento.Registrar("1", Cenario.OsId, 191.00m, "visa", StatusPagamento.Recusado,
            "cc_rejected_other_reason", RelogioFixo.Agora);
        var estornado = Pagamento.Registrar("2", Cenario.OsId, 191.00m, "pix", StatusPagamento.Aprovado, null,
            RelogioFixo.Agora.AddMinutes(5));
        estornado.Estornar("est-1", RelogioFixo.Agora.AddMinutes(10));
        _repo.Setup(r => r.ListarPagamentosAsync(Cenario.OsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { estornado, recusado });

        var r = await Criar().ExecutarAsync(Cenario.OsId, Cenario.Dono, default);

        r.Should().Equal(
            new PagamentoResposta("1", "Recusado", 191.00m, "visa", "cc_rejected_other_reason", null,
                RelogioFixo.Agora, RelogioFixo.Agora),
            new PagamentoResposta("2", "Estornado", 191.00m, "pix", null, "est-1",
                RelogioFixo.Agora.AddMinutes(5), RelogioFixo.Agora.AddMinutes(10)));
    }

    [Fact]
    public async Task ClienteNaoDono_LancaNaoEncontrado()
    {
        _repo.Setup(r => r.ObterOrcamentoAsync(Cenario.OsId, It.IsAny<CancellationToken>())).ReturnsAsync(Cenario.Orcamento());

        var act = () => Criar().ExecutarAsync(Cenario.OsId, Cenario.Outro, default);

        await act.Should().ThrowAsync<OrcamentoNaoEncontradoException>();
        _repo.Verify(r => r.ListarPagamentosAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrcamentoInexistente_LancaNaoEncontrado()
    {
        var act = () => Criar().ExecutarAsync(Cenario.OsId, Cenario.Atendente, default);

        await act.Should().ThrowAsync<OrcamentoNaoEncontradoException>();
    }
}
