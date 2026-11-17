# Manifestos Kubernetes — Serviço de Billing

Orquestração do `oficina-billing` no EKS (Fase 4), no mesmo cluster e namespaces do
`oficina-app`: `oficina-hml` (branch `develop`) e `oficina-prd` (branch `main`). Os
namespaces **não** são criados aqui: vêm do CD do `oficina-app` (a access entry da role
de deploy é restrita a eles).

| Arquivo | Recurso | O que cria |
|---|---|---|
| `serviceaccount.yaml` | ServiceAccount | `oficina-billing`, com a role IRSA (`${IRSA_ROLE_ARN}`) para SQS e DynamoDB. |
| `configmap.yaml` | ConfigMap | `oficina-billing-config`: mensageria SQS, tabela DynamoDB, modo de pagamento, URL de notificação do Mercado Pago. |
| `secret.yaml` | Secret | `oficina-billing-secret`: **TEMPLATE** (não aplicado); o CD cria o real a partir do Secrets Manager. |
| `deployment.yaml` | Deployment | 2 réplicas, probes em `/health:8080`, rootfs read-only, uid/gid 1654 numérico, New Relic ligado. O pod recebe `HOSTNAME` do Kubernetes (fila SQS por instância). |
| `service.yaml` | Service | `NodePort` 80 → 8080; `30082` (prd) / `30083` (hml). |
| `hpa.yaml` | HPA | 2–5 réplicas, CPU 60%. |

Variáveis substituídas por `envsubst` no CD: `NAMESPACE`, `AMBIENTE` (`hml`|`prd`),
`NODE_PORT`, `ECR_REPOSITORY`, `IMAGE_TAG`, `IRSA_ROLE_ARN`, `TABELA_DYNAMODB`,
`PAGAMENTO_MODO`, `URL_NOTIFICACAO`.

Modo de pagamento: `hml` é sempre `Simulado`; `prd` usa `MercadoPago`, exceto enquanto os
segredos do Mercado Pago forem o placeholder `TROCAR...` do Terraform (cai em `Simulado`
com aviso).

O `cd.yml` pula o deploy (aviso, sem falhar) enquanto o secret `AWS_DEPLOY_ROLE_ARN` não
existir — a role só nasce após o primeiro apply manual do `terraform/`.
