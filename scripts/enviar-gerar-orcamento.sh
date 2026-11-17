#!/usr/bin/env bash
# Envia um comando GerarOrcamento (envelope MassTransit) para a fila de comandos no LocalStack.
# Uso: ./scripts/enviar-gerar-orcamento.sh [osId]      (usa o aws CLI local; sem ele, rode via docker compose exec localstack awslocal)
set -euo pipefail

os_id="${1:-3f2b8c1e-6a4d-4e7b-9c3a-1d2e3f4a5b6c}"
fila="local-oficina-billing-comandos"
endpoint="http://localhost:4566"
export AWS_ACCESS_KEY_ID="${AWS_ACCESS_KEY_ID:-test}"
export AWS_SECRET_ACCESS_KEY="${AWS_SECRET_ACCESS_KEY:-test}"
export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-us-east-1}"

# A fila so existe depois que o MassTransit da API inicia.
fila_url=""
for _ in $(seq 1 30); do
  if fila_url=$(aws --endpoint-url "$endpoint" sqs get-queue-url --queue-name "$fila" --query QueueUrl --output text 2>/dev/null); then
    break
  fi
  fila_url=""
  sleep 2
done
if [[ -z "$fila_url" ]]; then
  echo "ERRO: a fila '$fila' nao existe apos 60s. A API esta no ar? (docker compose --env-file .env -f docker/docker-compose.yml up -d --wait)" >&2
  exit 1
fi

novo_guid() {
  cat /proc/sys/kernel/random/uuid 2>/dev/null || uuidgen 2>/dev/null     || powershell.exe -NoProfile -Command '[guid]::NewGuid().ToString()' | tr -d '\r'
}

message_id=$(novo_guid)
agora=$(date -u +%Y-%m-%dT%H:%M:%SZ)
referencia_servico=$(novo_guid)
referencia_peca=$(novo_guid)
corpo=$(printf '{"ordemServicoId":"%s","ocorridoEm":"%s","numeroOs":1042,"clienteDocumento":"12345678909","clienteNome":"Cliente Dev","clienteEmail":"cliente@example.com","itens":[{"tipo":"Servico","referenciaId":"%s","descricao":"Troca de oleo","quantidade":1,"precoUnitario":120.00},{"tipo":"Peca","referenciaId":"%s","descricao":"Filtro de oleo","quantidade":2,"precoUnitario":35.50}]}' \
  "$os_id" "$agora" "$referencia_servico" "$referencia_peca")
envelope=$(printf '{"messageId":"%s","messageType":["urn:message:oficina:saga:GerarOrcamento"],"message":%s}' "$message_id" "$corpo")

aws --endpoint-url "$endpoint" sqs send-message \
  --queue-url "$fila_url" \
  --message-body "$envelope" \
  --message-attributes '{"Content-Type":{"DataType":"String","StringValue":"application/vnd.masstransit+json"}}'
