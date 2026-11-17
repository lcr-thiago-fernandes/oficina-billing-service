# language: pt
Funcionalidade: Orçamento e pagamento da ordem de serviço
  Como participante da saga da OS, o Billing gera o orçamento, recebe a decisão do
  cliente, confirma ou recusa o pagamento e estorna quando a saga compensa.

  Contexto:
    Dado que o OS Service solicitou o orçamento da OS 1042 do cliente "52998224725" com os itens:
      | tipo    | descricao      | quantidade | precoUnitario |
      | Servico | Troca de oleo  | 1          | 120.00        |
      | Peca    | Filtro de oleo | 2          | 35.50         |
    Então o evento "OrcamentoGerado" é publicado com valor total 191.00

  Cenário: Cliente aprova e o pagamento é aprovado
    Quando o cliente "52998224725" aprova o orçamento
    Então o evento "OrcamentoAprovado" é publicado com link de pagamento
    Quando o pagamento é "aprovado" no gateway
    Então o evento "PagamentoConfirmado" é publicado com valor 191.00
    E o pagamento da OS fica com status "Aprovado"

  Cenário: Cliente aprova e o pagamento é recusado
    Quando o cliente "52998224725" aprova o orçamento
    E o pagamento é "recusado" no gateway
    Então o evento "PagamentoRecusado" é publicado
    E o orçamento fica com status "Cancelado"

  Cenário: Pagamento aprovado é estornado quando a execução recusa a OS
    Quando o cliente "52998224725" aprova o orçamento
    E o pagamento é "aprovado" no gateway
    E o OS Service solicita o estorno com motivo "Execucao recusada: capacidade da unidade esgotada"
    Então o evento "PagamentoEstornado" é publicado com valor 191.00
    E o pagamento da OS fica com status "Estornado"

  Cenário: Cliente rejeita o orçamento
    Quando o cliente "52998224725" rejeita o orçamento com motivo "Cliente achou caro"
    Então o evento "OrcamentoRejeitado" é publicado
