# Uma tabela por ambiente (oficina-billing-prd / oficina-billing-hml), chave pk/sk:
#   pk=OS#<osId>       sk=ORCAMENTO
#   pk=OS#<osId>       sk=PAGAMENTO#<pagamentoId>
#   pk=WEBHOOK#<id>    sk=WEBHOOK   (dedupe do webhook; expira via TTL em expiraEm, 90 dias)
resource "aws_dynamodb_table" "billing" {
  for_each = local.ambientes

  name         = "${local.nome_servico}-${each.key}"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "pk"
  range_key    = "sk"

  attribute {
    name = "pk"
    type = "S"
  }

  attribute {
    name = "sk"
    type = "S"
  }

  # expiraEm: epoch em segundos (Number), gravado so nos itens de dedupe do webhook.
  ttl {
    attribute_name = "expiraEm"
    enabled        = true
  }

  point_in_time_recovery {
    enabled = true
  }

  server_side_encryption {
    enabled = true
  }
}
