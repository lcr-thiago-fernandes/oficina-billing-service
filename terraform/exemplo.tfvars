# Copie para local.tfvars (ignorado pelo git) para aplicar da propria maquina.
# Pre-requisito: os parametros SSM consumidos em data.tf ja publicados pelo
# oficina-infra-k8s e pelo oficina-lambda-auth.

region  = "us-east-1"
project = "oficina"

github_owner = "lcr-thiago-fernandes"
github_repo  = "oficina-billing-service"

ecr_keep_last_images = 20
