using Oficina.Billing.Dominio.Comum;

namespace Oficina.Billing.Dominio.Orcamentos;

public class Orcamento
{
    private Orcamento(Guid id, Guid ordemServicoId, int numeroOs, DadosCliente cliente,
        IReadOnlyList<ItemOrcamento> itens, decimal valorTotal, StatusOrcamento status, string? motivo,
        string? preferenciaId, string? linkPagamento, DateTime criadoEm, DateTime atualizadoEm, int versao)
    {
        Id = id;
        OrdemServicoId = ordemServicoId;
        NumeroOs = numeroOs;
        Cliente = cliente;
        Itens = itens;
        ValorTotal = valorTotal;
        Status = status;
        Motivo = motivo;
        PreferenciaId = preferenciaId;
        LinkPagamento = linkPagamento;
        CriadoEm = criadoEm;
        AtualizadoEm = atualizadoEm;
        Versao = versao;
    }

    public Guid Id { get; }
    public Guid OrdemServicoId { get; }
    public int NumeroOs { get; }
    public DadosCliente Cliente { get; }
    public IReadOnlyList<ItemOrcamento> Itens { get; }
    public decimal ValorTotal { get; }
    public StatusOrcamento Status { get; private set; }
    public string? Motivo { get; private set; }
    public string? PreferenciaId { get; private set; }
    public string? LinkPagamento { get; private set; }
    public DateTime CriadoEm { get; }
    public DateTime AtualizadoEm { get; private set; }
    public int Versao { get; private set; }

    public static Orcamento Gerar(Guid ordemServicoId, int numeroOs, DadosCliente cliente,
        IEnumerable<ItemOrcamento> itens, DateTime agora)
    {
        if (ordemServicoId == Guid.Empty)
            throw new OrcamentoInvalidoException("OrdemServicoId vazio");
        var documento = DocumentoBr.Normalizar(cliente.Documento);
        if (documento.Length == 0)
            throw new OrcamentoInvalidoException("Documento do cliente vazio");

        var lista = itens.ToList();
        if (lista.Count == 0)
            throw new OrcamentoInvalidoException("Orcamento sem itens");
        foreach (var item in lista)
        {
            if (item.Quantidade <= 0)
                throw new OrcamentoInvalidoException("Quantidade deve ser positiva");
            if (item.PrecoUnitario < 0)
                throw new OrcamentoInvalidoException("Preco unitario nao pode ser negativo");
        }

        var total = Math.Round(lista.Sum(i => i.Subtotal), 2);
        return new Orcamento(Guid.NewGuid(), ordemServicoId, numeroOs, cliente with { Documento = documento },
            lista.AsReadOnly(), total, StatusOrcamento.Gerado, null, null, null, agora, agora, 0);
    }

    public static Orcamento Reconstituir(Guid id, Guid ordemServicoId, int numeroOs, DadosCliente cliente,
        IReadOnlyList<ItemOrcamento> itens, decimal valorTotal, StatusOrcamento status, string? motivo,
        string? preferenciaId, string? linkPagamento, DateTime criadoEm, DateTime atualizadoEm, int versao) =>
        new(id, ordemServicoId, numeroOs, cliente, itens, valorTotal, status, motivo, preferenciaId,
            linkPagamento, criadoEm, atualizadoEm, versao);

    public void Aprovar(string preferenciaId, string linkPagamento, DateTime agora)
    {
        ExigirGerado(StatusOrcamento.Aprovado);
        Status = StatusOrcamento.Aprovado;
        PreferenciaId = preferenciaId;
        LinkPagamento = linkPagamento;
        AtualizadoEm = agora;
    }

    public void Rejeitar(string? motivo, DateTime agora)
    {
        ExigirGerado(StatusOrcamento.Rejeitado);
        Status = StatusOrcamento.Rejeitado;
        Motivo = motivo;
        AtualizadoEm = agora;
    }

    /// <returns>true se cancelou; false se ja estava em estado terminal (sem alteracao).</returns>
    public bool Cancelar(string motivo, DateTime agora)
    {
        if (Status is StatusOrcamento.Cancelado or StatusOrcamento.Rejeitado)
            return false;
        Status = StatusOrcamento.Cancelado;
        Motivo = motivo;
        AtualizadoEm = agora;
        return true;
    }

    public void IncrementarVersao() => Versao++;

    private void ExigirGerado(StatusOrcamento para)
    {
        if (Status != StatusOrcamento.Gerado)
            throw new TransicaoInvalidaException(nameof(Orcamento), Status.ToString(), para.ToString());
    }
}
