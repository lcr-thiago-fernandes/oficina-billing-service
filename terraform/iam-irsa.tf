# IRSA: uma role por ambiente, assumida SO pela ServiceAccount oficina-billing do
# namespace do ambiente (k8s/serviceaccount.yaml; o OIDC provider do EKS e do
# oficina-infra-k8s, lido do SSM). Escopo por prefixo de ambiente: hml nunca toca
# fila/topico/tabela de prd e vice-versa.
#
# Sem secretsmanager (decisao D6, desvio consciente da secao 3 da spec): os segredos
# chegam ao pod pelo Secret oficina-billing-secret criado pelo cd.yml, como no oficina-app.

data "aws_iam_policy_document" "irsa_trust" {
  for_each = local.ambientes

  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [local.oidc_provider_arn]
    }

    condition {
      test     = "StringEquals"
      variable = "${local.oidc_provider_url}:sub"
      values   = ["system:serviceaccount:${each.value.namespace}:${local.service_account}"]
    }

    condition {
      test     = "StringEquals"
      variable = "${local.oidc_provider_url}:aud"
      values   = ["sts.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "irsa" {
  for_each = local.ambientes

  name               = "${local.nome_servico}-pods-${each.key}"
  description        = "IRSA do oficina-billing em ${each.value.namespace}: DynamoDB, SQS e SNS do ambiente ${each.key}."
  assume_role_policy = data.aws_iam_policy_document.irsa_trust[each.key].json
}

data "aws_iam_policy_document" "irsa" {
  for_each = local.ambientes

  # TransactWriteItems (orcamento/pagamento + dedupe do webhook) e autorizado pelas acoes
  # de cada item (PutItem/UpdateItem/ConditionCheckItem); a acao fica listada como pede o
  # plano. DescribeTable: espera de tabela ativa no inicializador.
  statement {
    sid    = "DynamoDbTabelaDoAmbiente"
    effect = "Allow"
    actions = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:UpdateItem",
      "dynamodb:Query",
      "dynamodb:TransactWriteItems",
      "dynamodb:ConditionCheckItem",
      "dynamodb:DescribeTable",
    ]
    resources = [aws_dynamodb_table.billing[each.key].arn]
  }

  # Filas do ambiente: comandos, _error/_skipped do MassTransit e a fila do bus por
  # instancia (${amb}-oficina-billing-bus-*, criada e apagada pelos pods: DeleteQueue).
  # SetQueueAttributes tambem grava a policy que permite ao SNS entregar na fila assinante.
  statement {
    sid    = "SqsFilasDoAmbiente"
    effect = "Allow"
    actions = [
      "sqs:CreateQueue",
      "sqs:DeleteQueue",
      "sqs:GetQueueUrl",
      "sqs:GetQueueAttributes",
      "sqs:SetQueueAttributes",
      "sqs:SendMessage",
      "sqs:ReceiveMessage",
      "sqs:DeleteMessage",
      "sqs:ChangeMessageVisibility",
      "sqs:PurgeQueue",
      "sqs:TagQueue",
      "sqs:ListQueueTags",
    ]
    resources = ["arn:aws:sqs:${var.region}:${local.conta}:${each.key}-${var.project}-*"]
  }

  # Topicos do ambiente: ${amb}-oficina-saga-<Nome> (SetEntityName por tipo).
  statement {
    sid    = "SnsTopicosDoAmbiente"
    effect = "Allow"
    actions = [
      "sns:CreateTopic",
      "sns:GetTopicAttributes",
      "sns:SetTopicAttributes",
      "sns:Subscribe",
      "sns:Unsubscribe",
      "sns:Publish",
      "sns:ListSubscriptionsByTopic",
      "sns:TagResource",
    ]
    resources = ["arn:aws:sns:${var.region}:${local.conta}:${each.key}-${var.project}-*"]
  }

  # Listagens sem escopo por recurso (N8 do plano: incluidas de saida; conferir no
  # primeiro run com CloudTrail / erros AccessDenied). ListSubscriptionsByTopic e
  # ListQueueTags tambem aqui, como pede o plano (ja cobertas acima no prefixo).
  statement {
    sid    = "Listagens"
    effect = "Allow"
    actions = [
      "sns:ListTopics",
      "sns:ListSubscriptionsByTopic",
      "sqs:ListQueues",
      "sqs:ListQueueTags",
    ]
    resources = ["*"]
  }
}

resource "aws_iam_role_policy" "irsa" {
  for_each = local.ambientes

  name   = "${local.nome_servico}-pods-${each.key}-policy"
  role   = aws_iam_role.irsa[each.key].id
  policy = data.aws_iam_policy_document.irsa[each.key].json
}
