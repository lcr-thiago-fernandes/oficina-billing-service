# Contratos entre repositórios

O Billing conversa com os outros repositórios por **quatro vias**: o token JWT, o SSM Parameter Store, o Secrets
Manager e as mensagens da saga. Cada item é um acoplamento que **falha em silêncio** se divergir (o deploy passa e o
sintoma aparece longe da causa), por isso está escrito aqui. O `terraform/` não usa `terraform_remote_state`: se um
parâmetro consumido não existir, o `plan` falha no `data "aws_ssm_parameter"` com o nome dele.

| Repositório | Relação com o Billing |
|---|---|
| `oficina-app` | OS Service e **orquestrador da saga**; dono da arquitetura central |
| `oficina-lambda-auth` | Emite o JWT e fornece o Lambda Authorizer do API Gateway |
| `oficina-infra-k8s` | VPC, EKS, NLB, API Gateway, VPC Link e New Relic |
| `oficina-estoque-service`, `oficina-execucao-service` | Outros participantes da saga (sem chamada direta ao Billing) |

## 1. Token JWT

Igual ao contrato do `oficina-app` (§1): o Billing só **valida**, nunca emite.

```
iss = oficina-auth     aud = oficina-api     alg = HS256
```

Claims: `sub`, `perfil` (`Cliente` | `Atendente` | `Admin`, case-sensitive), `documento` (obrigatória no perfil
`Cliente`), `nome`, `jti`, `exp`. O segredo é `oficina/jwt_secret` (Secrets Manager), o mesmo da Lambda. Cliente só
acessa o orçamento/pagamentos do próprio `documento`; não-dono recebe 404 e Cliente sem `documento` recebe 403.

## 2. Mensagens da saga

Serialização `System.Text.Json` com `JsonSerializerDefaults.Web` e enums como string. URN `oficina:saga:<Nome>` e
`EntityName` = nome do tipo. O prefixo do ambiente (`local | hml | prd`) é aplicado **por tipo** com `SetEntityName`
(spec §5), nunca por `PrefixEntityNameFormatter`. JSON de cada mensagem e `SHA256SUMS` em
[`contratos/mensagens/`](contratos/mensagens); os mesmos arquivos existem nos outros repositórios da saga e os testes
de contrato comparam o hash, então **alterar um contrato exige alterar todos os repositórios**.

| Direção | Mensagem | Destino / origem |
|---|---|---|
| Consome | `GerarOrcamento`, `CancelarOrcamento`, `EstornarPagamento` | fila `{prefixo}-oficina-billing-comandos` (envio direto do orquestrador; `_error` para falha final) |
| Publica | `OrcamentoGerado`, `OrcamentoFalhou`, `OrcamentoAprovado`, `OrcamentoRejeitado` | tópico `{prefixo}-oficina-saga-<Nome>` |
| Publica | `PagamentoConfirmado`, `PagamentoRecusado`, `PagamentoEstornado`, `EstornoFalhou` | tópico `{prefixo}-oficina-saga-<Nome>` |

Fila do bus: `{prefixo}-oficina-billing-bus-{instância}` (temporária, uma por réplica, nome do pod, truncada em 80
caracteres), dentro do padrão IAM `${amb}-oficina-*`.

## 3. Portas e rotas

| Ambiente | Namespace | NodePort | Listener do NLB | Exposição |
|---|---|---|---|---|
| prd | `oficina-prd` | 30082 | 8081 | API Gateway (VPC Link) |
| hml | `oficina-hml` | 30083 | 8082 | só de dentro da VPC (sempre modo `Simulado`) |

Rotas no HTTP API (só prd): `ANY /billing/api/v1/{proxy+}` (authorizer `CUSTOM`) e, sem authorizer,
`POST /billing/webhooks/mercadopago` (validado por `x-signature`), `GET /billing/health` e
`GET /billing/swagger/{proxy+}`.

## 4. SSM Parameter Store

### Consumidos, já existentes

| Parâmetro | Publicado por | Uso aqui |
|---|---|---|
| `/oficina/network/vpc_id` | `oficina-infra-k8s` | VPC dos target groups; CIDR da regra hml |
| `/oficina/network/eks_node_sg_id` | `oficina-infra-k8s` | Ingress dos NodePorts a partir do NLB |
| `/oficina/eks/cluster_name` | `oficina-infra-k8s` | Access entry da role de deploy |
| `/oficina/apigw/api_id` | `oficina-infra-k8s` | Integrações e rotas `/billing/*` |
| `/oficina/apigw/vpc_link_id` | `oficina-infra-k8s` | `connection_id` das integrações |
| `/oficina/auth/lambda_authorizer_id` | `oficina-lambda-auth` | Authorizer da rota protegida |

### Consumidos, NOVOS (a publicar pelo `oficina-infra-k8s`) — pendência

Ainda **não existem**: dependem do PR de SSM do `oficina-infra-k8s` e precisam estar publicados antes do primeiro
`plan`/`apply` deste repositório.

| Parâmetro | Tipo | Valor no `oficina-infra-k8s` | Uso aqui |
|---|---|---|---|
| `/oficina/nlb/arn` | String | ARN do NLB interno | Listeners 8081 / 8082 |
| `/oficina/nlb/security_group_id` | String | SG do NLB | Regras de SG do NLB |
| `/oficina/apigw/vpc_link_security_group_id` | String | SG do VPC Link | Egress 8081 do VPC Link para o NLB |
| `/oficina/eks/node_asg_names` | StringList | ASGs do node group, separados por vírgula | `aws_autoscaling_attachment` (ASG × ambiente) |
| `/oficina/eks/oidc_provider_arn` | String | ARN do provedor OIDC do EKS | `Federated` da trust IRSA |
| `/oficina/eks/oidc_provider_url` | String | URL do OIDC **sem** `https://` (o código aplica `trimprefix` por defesa) | Chaves `:sub`/`:aud` da trust IRSA |

### Publicados por este repositório (`/oficina/billing/`)

| Parâmetro | Valor | Lido por |
|---|---|---|
| `/oficina/billing/deploy_role_arn` | Role `oficina-gha-deploy-billing` | operador (`gh secret set AWS_DEPLOY_ROLE_ARN`) |
| `/oficina/billing/ecr_repository_url` | URL do ECR `oficina-billing` | `cd.yml` |
| `/oficina/billing/{prd,hml}/irsa_role_arn` | Role `oficina-billing-pods-{prd,hml}` | `cd.yml` (anotação da ServiceAccount) |
| `/oficina/billing/{prd,hml}/tabela_dynamodb` | `oficina-billing-{prd,hml}` | `cd.yml` |
| `/oficina/billing/url_notificacao_mercadopago` | `<api_endpoint>/billing/webhooks/mercadopago` | `cd.yml` (`MercadoPago__UrlNotificacao`) |

## 5. Segredos (Secrets Manager)

| Segredo | Quem escreve | Uso |
|---|---|---|
| `oficina/jwt_secret` | `oficina-infra-k8s` | `Jwt__Secret` (lido pelo `cd.yml`) |
| `oficina/newrelic_license_key` | `oficina-infra-k8s` | `NewRelic__LicenseKey` |
| `oficina/billing/mercadopago_access_token` | Terraform (placeholder `TROCAR_NO_CONSOLE`); valor real colocado manualmente | `MercadoPago__AccessToken` |
| `oficina/billing/mercadopago_webhook_secret` | idem | `MercadoPago__WebhookSecret` |

O `cd.yml` cria o Secret do K8s a partir desses valores; o IRSA do pod **não** tem permissão de `secretsmanager`.
Enquanto os segredos do Mercado Pago forem o placeholder, prd sobe em modo `Simulado` com aviso.

## 6. Evento `PagamentoEvento` (New Relic)

Evento customizado gravado em cada passo, consumido pelo dashboard "Saga da OS" (`oficina-infra-k8s`).

Atributos: `ordemServicoId`, `numeroOs`, `etapa`, `resultado` (`Sucesso` | `Falha`), `gateway` (`MercadoPago`,
`Simulado` ou `-` quando não há cobrança), `valor` e `meio` (quando existem).

| `etapa` | `resultado` |
|---|---|
| `OrcamentoGerado` | `Sucesso` |
| `OrcamentoFalhou` | `Falha` |
| `OrcamentoAprovado` | `Sucesso` |
| `OrcamentoRejeitado` | `Sucesso` |
| `OrcamentoCancelado` | `Sucesso` |
| `PagamentoConfirmado` | `Sucesso` |
| `PagamentoRecusado` | `Falha` |
| `PagamentoEstornado` | `Sucesso` |
| `EstornoFalhou` | `Falha` |
| `EstornoAutomatico` | `Sucesso` ou `Falha` (estorno de pagamento inaceitável) |

## 7. Pendências entre repositórios

1. **`oficina-infra-k8s` (PR de SSM):** publicar os 6 parâmetros da seção 4 (`/oficina/nlb/arn`,
   `/oficina/nlb/security_group_id`, `/oficina/apigw/vpc_link_security_group_id`, `/oficina/eks/node_asg_names`,
   `/oficina/eks/oidc_provider_arn`, `/oficina/eks/oidc_provider_url`).
2. **`oficina-infra-k8s/scripts/bootstrap.sh`:** incluir `oficina-billing-service` em `REPOS_INFRA`, para a trust da role
   `oficina-gha-infra` (secret `AWS_TERRAFORM_ROLE_ARN` do `infra.yml`) aceitar `main`/`develop` deste repositório.
3. **Observabilidade (`oficina-infra-k8s`):** painel e alerta para `PagamentoEvento` e para a fila
   `prd-oficina-billing-comandos_error` com mensagens.
4. **Ordem de subida:** `oficina-infra-k8s` → `oficina-lambda-auth` → `terraform/` deste repositório (`infra.yml` com
   `workflow_dispatch`, `acao=apply`, em `main`) → `gh secret set AWS_DEPLOY_ROLE_ARN` com `/oficina/billing/deploy_role_arn`
   → primeiro CD → segredos reais do Mercado Pago e novo CD.
