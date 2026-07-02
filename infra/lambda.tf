data "archive_file" "lambda_bootstrap" {
  type        = "zip"
  source_file = "${path.module}/lambda-bootstrap/placeholder.txt"
  output_path = "${path.module}/.terraform/lambda-bootstrap.zip"
}

data "archive_file" "admin_claims" {
  type        = "zip"
  source_file = "${path.module}/lambda-admin-claims/index.mjs"
  output_path = "${path.module}/.terraform/admin-claims.zip"
}

locals {
  lambda_functions = {
    api = {
      handler     = "Bolao.Functions::Bolao.Functions.LambdaEntryPoint::FunctionHandlerAsync"
      timeout     = 30
      memory_size = 512
    }
    retention = {
      handler     = "Bolao.Functions::Bolao.Functions.Jobs.DataRetentionHandler::HandleAsync"
      timeout     = 60
      memory_size = 256
    }
  }

  table_arns = [for table in aws_dynamodb_table.this : table.arn]
  lambda_actions = {
    api = [
      "dynamodb:DeleteItem",
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:Query",
      "dynamodb:Scan",
      "dynamodb:TransactWriteItems",
      "dynamodb:UpdateItem"
    ]
    retention = [
      "dynamodb:DeleteItem",
      "dynamodb:GetItem",
      "dynamodb:Query",
      "dynamodb:Scan",
      "dynamodb:UpdateItem"
    ]
  }
}

resource "aws_iam_role" "lambda" {
  for_each = local.lambda_functions

  name = "${local.name_prefix}-${replace(each.key, "_", "-")}-lambda-role"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Principal = {
        Service = "lambda.amazonaws.com"
      }
      Action = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy" "lambda_logs" {
  for_each = local.lambda_functions

  name = "${local.name_prefix}-${replace(each.key, "_", "-")}-lambda-logs"
  role = aws_iam_role.lambda[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "logs:CreateLogGroup",
        "logs:CreateLogStream",
        "logs:PutLogEvents"
      ]
      Resource = "arn:${data.aws_partition.current.partition}:logs:${var.aws_region}:${data.aws_caller_identity.current.account_id}:*"
    }]
  })
}

resource "aws_iam_role_policy" "lambda_dynamodb" {
  for_each = local.lambda_functions

  name = "${local.name_prefix}-${replace(each.key, "_", "-")}-lambda-dynamodb"
  role = aws_iam_role.lambda[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = local.lambda_actions[each.key]
      Resource = concat(local.table_arns, [for arn in local.table_arns : "${arn}/index/*"])
    }]
  })
}

resource "aws_iam_role_policy" "api_cognito" {
  name = "${local.name_prefix}-api-cognito"
  role = aws_iam_role.lambda["api"].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "cognito-idp:AdminGetUser",
        "cognito-idp:AdminUpdateUserAttributes"
      ]
      Resource = aws_cognito_user_pool.main.arn
    }]
  })
}

resource "aws_iam_role_policy" "api_ses" {
  count = local.ses_identity_arn == null ? 0 : 1

  name = "${local.name_prefix}-api-ses"
  role = aws_iam_role.lambda["api"].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["ses:SendEmail"]
      Resource = local.ses_identity_arn
    }]
  })
}

resource "aws_iam_role_policy" "retention_cognito" {
  name = "${local.name_prefix}-retention-cognito"
  role = aws_iam_role.lambda["retention"].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["cognito-idp:AdminDeleteUser"]
      Resource = aws_cognito_user_pool.main.arn
    }]
  })
}

resource "aws_lambda_function" "this" {
  for_each = local.lambda_functions

  function_name    = "${local.name_prefix}-${replace(each.key, "_", "-")}"
  role             = aws_iam_role.lambda[each.key].arn
  runtime          = "dotnet10"
  handler          = each.value.handler
  filename         = data.archive_file.lambda_bootstrap.output_path
  source_code_hash = data.archive_file.lambda_bootstrap.output_base64sha256
  timeout          = each.value.timeout
  memory_size      = each.value.memory_size

  environment {
    variables = merge({
      PARTICIPANTS_TABLE_NAME = aws_dynamodb_table.this["participants"].name
      MATCHES_TABLE_NAME      = aws_dynamodb_table.this["matches"].name
      PREDICTIONS_TABLE_NAME  = aws_dynamodb_table.this["predictions"].name
      STANDINGS_TABLE_NAME    = aws_dynamodb_table.this["standings"].name
      COGNITO_USER_POOL_ID    = aws_cognito_user_pool.main.id
      }, each.key == "api" && local.ses_from_email != null ? {
      SES_FROM_EMAIL = local.ses_from_email
    } : {})
  }

  lifecycle {
    ignore_changes = [filename, source_code_hash]
  }
}

resource "aws_iam_role" "admin_claims" {
  name = "${local.name_prefix}-admin-claims-lambda-role"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Principal = {
        Service = "lambda.amazonaws.com"
      }
      Action = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy" "admin_claims_logs" {
  name = "${local.name_prefix}-admin-claims-lambda-logs"
  role = aws_iam_role.admin_claims.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "logs:CreateLogGroup",
        "logs:CreateLogStream",
        "logs:PutLogEvents"
      ]
      Resource = "arn:${data.aws_partition.current.partition}:logs:${var.aws_region}:${data.aws_caller_identity.current.account_id}:*"
    }]
  })
}

resource "aws_lambda_function" "admin_claims" {
  function_name    = "${local.name_prefix}-admin-claims"
  role             = aws_iam_role.admin_claims.arn
  runtime          = "nodejs22.x"
  handler          = "index.handler"
  filename         = data.archive_file.admin_claims.output_path
  source_code_hash = data.archive_file.admin_claims.output_base64sha256
  timeout          = 5
  memory_size      = 128

  environment {
    variables = {
      ADMIN_EMAILS = jsonencode(sort([
        for email in var.admin_emails : lower(trimspace(email))
      ]))
    }
  }
}

resource "aws_lambda_permission" "cognito_admin_claims" {
  statement_id  = "AllowCognitoAdminClaims"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.admin_claims.function_name
  principal     = "cognito-idp.amazonaws.com"
  source_arn    = aws_cognito_user_pool.main.arn
}
