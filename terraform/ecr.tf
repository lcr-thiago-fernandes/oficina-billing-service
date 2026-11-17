resource "aws_ecr_repository" "billing" {
  name                 = local.nome_servico
  image_tag_mutability = "IMMUTABLE" # tag = SHA do commit (cd.yml): nunca sobrescrita.
  force_delete         = true        # demo: destroy mesmo com imagens.

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
