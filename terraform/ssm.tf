# Contrato publicado por este repositorio, sob /oficina/billing/.
# Lidos pelo cd.yml: ecr_repository_url, <amb>/irsa_role_arn, <amb>/tabela_dynamodb e
# url_notificacao_mercadopago.

resource "aws_ssm_parameter" "deploy_role_arn" {
  name        = "/${var.project}/billing/deploy_role_arn"
  description = "Role de deploy do oficina-billing-service (-> secret AWS_DEPLOY_ROLE_ARN)."
  type        = "String"
  value       = aws_iam_role.gha_deploy.arn
}

resource "aws_ssm_parameter" "ecr_repository_url" {
  name        = "/${var.project}/billing/ecr_repository_url"
  description = "URL do repositorio ECR do oficina-billing (lida pelo cd.yml)."
  type        = "String"
  value       = aws_ecr_repository.billing.repository_url
}

resource "aws_ssm_parameter" "irsa_role_arn" {
  for_each = local.ambientes

  name        = "/${var.project}/billing/${each.key}/irsa_role_arn"
  description = "Role IRSA da ServiceAccount oficina-billing em ${each.value.namespace} (lida pelo cd.yml)."
  type        = "String"
  value       = aws_iam_role.irsa[each.key].arn
}

resource "aws_ssm_parameter" "tabela_dynamodb" {
  for_each = local.ambientes

  name        = "/${var.project}/billing/${each.key}/tabela_dynamodb"
  description = "Tabela DynamoDB do oficina-billing no ambiente ${each.key} (lida pelo cd.yml)."
  type        = "String"
  value       = aws_dynamodb_table.billing[each.key].name
}

# notification_url enviada ao Mercado Pago na criacao do pagamento (so prd passa pelo
# gateway; em hml o modo e sempre Simulado).
resource "aws_ssm_parameter" "url_notificacao_mercadopago" {
  name        = "/${var.project}/billing/url_notificacao_mercadopago"
  description = "URL publica do webhook do Mercado Pago no API Gateway (lida pelo cd.yml)."
  type        = "String"
  value       = "${data.aws_apigatewayv2_api.oficina.api_endpoint}/billing/webhooks/mercadopago"
}
