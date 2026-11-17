output "gha_deploy_role_arn" {
  description = "-> secret AWS_DEPLOY_ROLE_ARN deste repositorio (cd.yml). Tambem em /oficina/billing/deploy_role_arn."
  value       = aws_iam_role.gha_deploy.arn
}

output "ecr_repository_url" {
  value = aws_ecr_repository.billing.repository_url
}

output "irsa_role_arns" {
  description = "Role IRSA por ambiente (tambem em /oficina/billing/<amb>/irsa_role_arn)."
  value       = { for amb, role in aws_iam_role.irsa : amb => role.arn }
}

output "tabelas_dynamodb" {
  value = { for amb, tabela in aws_dynamodb_table.billing : amb => tabela.name }
}

output "url_notificacao_mercadopago" {
  value = aws_ssm_parameter.url_notificacao_mercadopago.value
}
