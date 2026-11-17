# O HTTP API, o stage $default e o VPC Link sao do oficina-infra-k8s; o authorizer e do
# oficina-lambda-auth (ids lidos do SSM). Este repositorio pendura as integracoes com o
# listener PRD do servico (8081) e as rotas sob /billing. So prd (decisao D7, igual a
# Fase 3): hml nao passa pelo gateway. O HTTP_PROXY repassa o path inteiro: a API usa
# PathBase /billing.

# Integracao da rota PROTEGIDA: mapeia o contexto do authorizer em headers informativos,
# como a integracao do infra-k8s; o servico revalida o JWT por conta propria.
resource "aws_apigatewayv2_integration" "billing_protegida" {
  api_id                 = local.api_id
  integration_type       = "HTTP_PROXY"
  integration_method     = "ANY"
  integration_uri        = aws_lb_listener.billing["prd"].arn
  connection_type        = "VPC_LINK"
  connection_id          = local.vpc_link_id
  payload_format_version = "1.0"
  timeout_milliseconds   = 29000

  request_parameters = {
    "append:header.X-Perfil"    = "$context.authorizer.perfil"
    "append:header.X-Sub"       = "$context.authorizer.sub"
    "append:header.X-Documento" = "$context.authorizer.documento"
  }
}

# Integracao das rotas PUBLICAS: mesmo listener, SEM o mapeamento de
# $context.authorizer.* (nessas rotas o contexto nao existe e o mapeamento nao resolvido
# poderia virar 500 justamente no webhook e no /health).
resource "aws_apigatewayv2_integration" "billing_publica" {
  api_id                 = local.api_id
  integration_type       = "HTTP_PROXY"
  integration_method     = "ANY"
  integration_uri        = aws_lb_listener.billing["prd"].arn
  connection_type        = "VPC_LINK"
  connection_id          = local.vpc_link_id
  payload_format_version = "1.0"
  timeout_milliseconds   = 29000
}

resource "aws_apigatewayv2_route" "billing_protegida" {
  api_id             = local.api_id
  route_key          = "ANY /billing/api/v1/{proxy+}"
  target             = "integrations/${aws_apigatewayv2_integration.billing_protegida.id}"
  authorization_type = "CUSTOM"
  authorizer_id      = local.authorizer_id
}

# Sem authorizer: o webhook do Mercado Pago (autenticado pela assinatura x-signature,
# validada no servico), o /health e o Swagger (decisao D8: Swagger atras do gateway).
locals {
  rotas_publicas = {
    webhook_mercadopago = "POST /billing/webhooks/mercadopago"
    health              = "GET /billing/health"
    swagger             = "GET /billing/swagger" # {proxy+} nao casa com /swagger sem sufixo (redirect para index.html)
    swagger_proxy       = "GET /billing/swagger/{proxy+}"
  }
}

resource "aws_apigatewayv2_route" "billing_publica" {
  for_each = local.rotas_publicas

  api_id    = local.api_id
  route_key = each.value
  target    = "integrations/${aws_apigatewayv2_integration.billing_publica.id}"
}
