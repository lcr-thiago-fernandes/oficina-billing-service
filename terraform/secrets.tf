# Credenciais do Mercado Pago (texto puro, lidas pelo cd.yml com --query SecretString
# --output text). O Terraform cria so o placeholder TROCAR_NO_CONSOLE: o valor real e
# colocado no console e o Terraform nao o sobrescreve (ignore_changes). Enquanto for
# placeholder, o cd.yml sobe prd com Pagamento__Modo=Simulado.
locals {
  segredos_mercadopago = {
    access_token   = "Access token do Mercado Pago (Bearer da API de pagamentos e estornos)."
    webhook_secret = "Segredo de assinatura (x-signature) dos webhooks do Mercado Pago."
  }
}

resource "aws_secretsmanager_secret" "mercadopago" {
  for_each = local.segredos_mercadopago

  name                    = "${var.project}/billing/mercadopago_${each.key}"
  description             = each.value
  recovery_window_in_days = 0 # demo: destroy/recreate sem janela de recuperacao.
}

resource "aws_secretsmanager_secret_version" "mercadopago" {
  for_each = local.segredos_mercadopago

  secret_id     = aws_secretsmanager_secret.mercadopago[each.key].id
  secret_string = "TROCAR_NO_CONSOLE"

  lifecycle {
    ignore_changes = [secret_string]
  }
}
