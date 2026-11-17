# Role PROPRIA do CD deste servico (nao reusa oficina-gha-deploy do oficina-app): mesmo
# desenho, escopada ao ECR oficina-billing, ao SSM /oficina/billing/* e aos segredos de
# runtime. O acesso ao cluster vem da access entry (eks-access.tf).

# OIDC provider do GitHub: criado pelo scripts/bootstrap.sh do oficina-infra-k8s; so lido.
data "aws_iam_openid_connect_provider" "github" {
  url = "https://token.actions.githubusercontent.com"
}

locals {
  github_oidc_host = "token.actions.githubusercontent.com"
  # Trust escopada por BRANCH: so main (prd) e develop (hml). PR nao assume a role.
  subs_deploy = [
    "repo:${var.github_owner}/${var.github_repo}:ref:refs/heads/main",
    "repo:${var.github_owner}/${var.github_repo}:ref:refs/heads/develop",
  ]
}

data "aws_iam_policy_document" "trust_deploy" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [data.aws_iam_openid_connect_provider.github.arn]
    }

    condition {
      test     = "StringEquals"
      variable = "${local.github_oidc_host}:aud"
      values   = ["sts.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "${local.github_oidc_host}:sub"
      values   = local.subs_deploy
    }
  }
}

resource "aws_iam_role" "gha_deploy" {
  name               = local.nome_role_deploy
  description        = "CD do oficina-billing-service via OIDC: push no ECR oficina-billing, describe do EKS, SSM e segredos de runtime."
  assume_role_policy = data.aws_iam_policy_document.trust_deploy.json
}

data "aws_iam_policy_document" "deploy" {
  statement {
    sid       = "EcrAuth"
    effect    = "Allow"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"] # a acao exige "*".
  }

  statement {
    sid    = "EcrPushPull"
    effect = "Allow"
    actions = [
      "ecr:BatchCheckLayerAvailability",
      "ecr:GetDownloadUrlForLayer",
      "ecr:BatchGetImage",
      "ecr:PutImage",
      "ecr:InitiateLayerUpload",
      "ecr:UploadLayerPart",
      "ecr:CompleteLayerUpload",
    ]
    resources = [aws_ecr_repository.billing.arn]
  }

  # Kubeconfig (aws eks update-kubeconfig).
  statement {
    sid       = "EksDescribe"
    effect    = "Allow"
    actions   = ["eks:DescribeCluster"]
    resources = ["arn:aws:eks:${var.region}:${local.conta}:cluster/${local.cluster_name}"]
  }

  statement {
    sid       = "EksList"
    effect    = "Allow"
    actions   = ["eks:ListClusters"]
    resources = ["*"] # a acao nao aceita escopo por recurso.
  }

  # Parametros lidos pelo cd.yml: nome do cluster e /oficina/billing/* (ECR, IRSA,
  # tabela, URL de notificacao do Mercado Pago).
  statement {
    sid     = "SsmLeitura"
    effect  = "Allow"
    actions = ["ssm:GetParameter", "ssm:GetParameters"]
    resources = [
      "arn:aws:ssm:${var.region}:${local.conta}:parameter/${var.project}/eks/cluster_name",
      "arn:aws:ssm:${var.region}:${local.conta}:parameter/${var.project}/billing/*",
    ]
  }

  # Segredos que viram o Secret oficina-billing-secret. Os compartilhados sao do
  # oficina-infra-k8s (ARN por padrao de nome por causa do sufixo aleatorio do Secrets
  # Manager); os dois do Mercado Pago sao deste repositorio (secrets.tf).
  statement {
    sid     = "SegredosLeitura"
    effect  = "Allow"
    actions = ["secretsmanager:GetSecretValue"]
    resources = concat(
      [
        "arn:aws:secretsmanager:${var.region}:${local.conta}:secret:${var.project}/jwt_secret-*",
        "arn:aws:secretsmanager:${var.region}:${local.conta}:secret:${var.project}/newrelic_license_key-*",
      ],
      [for segredo in aws_secretsmanager_secret.mercadopago : segredo.arn],
    )
  }
}

resource "aws_iam_role_policy" "deploy" {
  name   = "${local.nome_role_deploy}-policy"
  role   = aws_iam_role.gha_deploy.id
  policy = data.aws_iam_policy_document.deploy.json
}
