using Oficina.Billing.Dominio.Comum;

namespace Oficina.Billing.Dominio.Pagamentos;

public class Pagamento
{
    private Pagamento(string id, Guid ordemServicoId, decimal valor, string meio, StatusPagamento status,
        string? detalheStatus, string? estornoId, DateTime criadoEm, DateTime atualizadoEm, int versao)
    {
        Id = id;
        OrdemServicoId = ordemServicoId;
        Valor = valor;
        Meio = meio;
        Status = status;
        DetalheStatus = detalheStatus;
        EstornoId = estornoId;
        CriadoEm = criadoEm;
        AtualizadoEm = atualizadoEm;
        Versao = versao;
    }

    public string Id { get; }
    public Guid OrdemServicoId { get; }
    public decimal Valor { get; }
    public string Meio { get; }
    public StatusPagamento Status { get; private set; }
    public string? DetalheStatus { get; private set; }
    public string? EstornoId { get; private set; }
    public DateTime CriadoEm { get; }
    public DateTime AtualizadoEm { get; private set; }
    public int Versao { get; private set; }

    public static Pagamento Registrar(string id, Guid ordemServicoId, decimal valor, string meio,
        StatusPagamento status, string? detalhe, DateTime agora)
    {
        if (status is not (StatusPagamento.Pendente or StatusPagamento.Aprovado or StatusPagamento.Recusado))
            throw new TransicaoInvalidaException(nameof(Pagamento), "(novo)", status.ToString());
        return new Pagamento(id, ordemServicoId, valor, meio, status, detalhe, null, agora, agora, 0);
    }

    public static Pagamento Reconstituir(string id, Guid ordemServicoId, decimal valor, string meio,
        StatusPagamento status, string? detalheStatus, string? estornoId, DateTime criadoEm,
        DateTime atualizadoEm, int versao) =>
        new(id, ordemServicoId, valor, meio, status, detalheStatus, estornoId, criadoEm, atualizadoEm, versao);

    public void Aprovar(DateTime agora)
    {
        ExigirStatus(StatusPagamento.Pendente, StatusPagamento.Aprovado);
        Status = StatusPagamento.Aprovado;
        AtualizadoEm = agora;
    }

    public void Recusar(string? detalhe, DateTime agora)
    {
        ExigirStatus(StatusPagamento.Pendente, StatusPagamento.Recusado);
        Status = StatusPagamento.Recusado;
        DetalheStatus = detalhe;
        AtualizadoEm = agora;
    }

    public void Estornar(string estornoId, DateTime agora)
    {
        ExigirStatus(StatusPagamento.Aprovado, StatusPagamento.Estornado);
        Status = StatusPagamento.Estornado;
        EstornoId = estornoId;
        AtualizadoEm = agora;
    }

    public void IncrementarVersao() => Versao++;

    private void ExigirStatus(StatusPagamento esperado, StatusPagamento para)
    {
        if (Status != esperado)
            throw new TransicaoInvalidaException(nameof(Pagamento), Status.ToString(), para.ToString());
    }
}
