locals {
  # Dois ambientes no MESMO cluster (um namespace cada). NodePort e listener do NLB sao
  # unicos no cluster/NLB: 30080/30081 e 80/81 = oficina-app; 30084/30085 e 8083/8084 =
  # Execucao. O API Gateway integra so com prd; hml so e alcancavel de dentro da VPC
  # (decisao D7, igual a Fase 3).
  ambientes = {
    prd = { nodeport = 30082, porta_listener = 8081, namespace = "oficina-prd" }
    hml = { nodeport = 30083, porta_listener = 8082, namespace = "oficina-hml" }
  }

  nome_servico     = "${var.project}-billing" # ECR, tabelas, target groups
  nome_role_deploy = "${var.project}-gha-deploy-billing"

  # ServiceAccount do k8s/serviceaccount.yaml (anotada com a role IRSA do ambiente).
  service_account = "${var.project}-billing"

  conta = data.aws_caller_identity.atual.account_id
}
