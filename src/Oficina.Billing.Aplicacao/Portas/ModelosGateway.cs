namespace Oficina.Billing.Aplicacao.Portas;

public record NovaCobranca(Guid OrdemServicoId, int NumeroOs, string? EmailPagador, IReadOnlyList<ItemCobranca> Itens);

public record ItemCobranca(string Titulo, int Quantidade, decimal PrecoUnitario);

public record CobrancaCriada(string PreferenciaId, string LinkPagamento);

public enum StatusPagamentoGateway { Pendente, Aprovado, Recusado, Estornado }

public record PagamentoGateway(string PagamentoId, Guid? OrdemServicoId, StatusPagamentoGateway Status,
    string? DetalheStatus, decimal Valor, string Meio);

public record EstornoGateway(string EstornoId, decimal Valor);

public class GatewayPagamentoIndisponivelException : Exception
{
    public GatewayPagamentoIndisponivelException(string mensagem) : base(mensagem) { }

    public GatewayPagamentoIndisponivelException(string mensagem, Exception? interna) : base(mensagem, interna) { }
}
