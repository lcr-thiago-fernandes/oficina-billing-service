resource "aws_ecr_repository" "billing" {
  name = local.nome_servico
  # MUTABLE (igual ao oficina-execucao-service): a tag e o SHA do commit (cd.yml), entao re-run do CD no mesmo commit
  # (Re-run jobs, workflow_dispatch, re-run apos preencher AWS_DEPLOY_ROLE_ARN ou trocar o segredo do MP) refaz o push
  # da mesma tag. Com IMMUTABLE o push falharia (ImageTagAlreadyExistsException) antes de atualizar Secret/ConfigMap.
  image_tag_mutability = "MUTABLE"
  force_delete         = true # demo: destroy mesmo com imagens.

  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_ecr_lifecycle_policy" "billing" {
  repository = aws_ecr_repository.billing.name

  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Manter apenas as ultimas ${var.ecr_keep_last_images} imagens"
      selection = {
        tagStatus   = "any"
        countType   = "imageCountMoreThan"
        countNumber = var.ecr_keep_last_images
      }
      action = { type = "expire" }
    }]
  })
}
