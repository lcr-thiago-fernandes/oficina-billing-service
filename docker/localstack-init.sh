#!/bin/sh
# Hook de init do LocalStack (montar em /etc/localstack/init/ready.d/). Nao cria recursos: o MassTransit da API
# cria a fila de comandos, a fila do bus e os topicos da saga. So registra a prontidao.
echo "LocalStack pronto: SQS e SNS disponiveis; filas e topicos serao criados pelo oficina-billing."
