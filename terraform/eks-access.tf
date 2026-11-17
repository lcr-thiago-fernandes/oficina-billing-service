# Acesso da role de deploy ao cluster RESTRITO aos namespaces dos ambientes. Por isso o
# cd.yml nao faz chamadas cluster-scoped (nem cria Namespace: sao do oficina-app).
# AmazonEKSEditPolicy (equivale a ClusterRole edit) cobre o que o cd.yml faz em cada
# namespace: get sa, apply de ServiceAccount, Secret, ConfigMap, Deployment, Service e
# HPA, e rollout status (watch de Deployment/ReplicaSet). Nao concede Role/RoleBinding.
resource "aws_eks_access_entry" "gha_deploy" {
  cluster_name  = local.cluster_name
  principal_arn = aws_iam_role.gha_deploy.arn
  type          = "STANDARD"
}

resource "aws_eks_access_policy_association" "gha_deploy" {
  cluster_name  = local.cluster_name
  principal_arn = aws_iam_role.gha_deploy.arn
  policy_arn    = "arn:aws:eks::aws:cluster-access-policy/AmazonEKSEditPolicy"

  access_scope {
    type       = "namespace"
    namespaces = ["oficina-prd", "oficina-hml"]
  }

  depends_on = [aws_eks_access_entry.gha_deploy]
}
