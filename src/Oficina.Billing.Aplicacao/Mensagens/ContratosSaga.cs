namespace Oficina.Billing.Aplicacao.Mensagens;

/// <summary>Os tipos de mensagem da saga que o Billing envia ou recebe.</summary>
public static class ContratosSaga
{
    public static readonly IReadOnlyList<Type> Tipos = new[]
    {
        typeof(GerarOrcamento),
        typeof(OrcamentoGerado),
        typeof(OrcamentoFalhou),
        typeof(OrcamentoAprovado),
        typeof(OrcamentoRejeitado),
        typeof(PagamentoConfirmado),
        typeof(PagamentoRecusado),
        typeof(EstornarPagamento),
        typeof(PagamentoEstornado),
        typeof(EstornoFalhou),
        typeof(CancelarOrcamento)
    };

    /// <summary>Nome do contrato (nome do tipo), usado tambem como nome de entidade no broker.</summary>
    public static string Nome(Type tipo) => tipo.Name;
}
