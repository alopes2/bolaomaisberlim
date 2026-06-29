data "archive_file" "lambda_bootstrap" {
  type        = "zip"
  source_file = "${path.module}/lambda-bootstrap/placeholder.txt"
  output_path = "${path.module}/.terraform/lambda-bootstrap.zip"
}

locals {
  lambda_functions = {
    api = {
      handler     = "Bolao.Functions::Bolao.Functions.LambdaEntryPoint::FunctionHandlerAsync"
      timeout     = 30
      memory_size = 512
    }
    daily_sync = {
      handler     = "Bolao.Functions::Bolao.Functions.Jobs.DailyMatchSyncHandler::HandleAsync"
      timeout     = 60
      memory_size = 512
    }
    match_polling = {
      handler     = "Bolao.Functions::Bolao.Functions.Jobs.MatchPollingHandler::HandleAsync"
      timeout     = 60
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
    daily_sync = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:Query",
      "dynamodb:UpdateItem"
    ]
    match_polling = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:Query",
      "dynamodb:TransactWriteItems",
      "dynamodb:UpdateItem"
    ]
    retention = [
      "dynamodb:DeleteItem",
      "dynamodb:GetItem",
      "dynamodb:Query",
      "dynamodb:UpdateItem"
    ]
  }
}

resource "aws_iam_role" "lambda" {
  for_each = local.lambda_functions

  name = "${local.name_prefix}-${replace(each.key, "_", "-")}-lambda"
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

  name = "logs"
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

  name = "dynamodb"
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
  name = "cognito-profile"
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
  count = var.ses_identity_arn == null ? 0 : 1

  name = "winner-email"
  role = aws_iam_role.lambda["api"].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["ses:SendEmail"]
      Resource = var.ses_identity_arn
    }]
  })
}

resource "aws_iam_role_policy" "retention_cognito" {
  name = "cognito-retention"
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
      PARTICIPANTS_TABLE_NAME    = aws_dynamodb_table.this["participants"].name
      MATCHES_TABLE_NAME         = aws_dynamodb_table.this["matches"].name
      PREDICTIONS_TABLE_NAME     = aws_dynamodb_table.this["predictions"].name
      STANDINGS_TABLE_NAME       = aws_dynamodb_table.this["standings"].name
      API_USAGE_TABLE_NAME       = aws_dynamodb_table.this["api_usage"].name
      COGNITO_USER_POOL_ID       = aws_cognito_user_pool.main.id
      SCHEDULER_GROUP_NAME       = aws_scheduler_schedule_group.matches.name
      MATCH_POLLING_FUNCTION_ARN = "arn:${data.aws_partition.current.partition}:lambda:${var.aws_region}:${data.aws_caller_identity.current.account_id}:function:${local.name_prefix}-match-polling"
      SCHEDULER_INVOKE_ROLE_ARN  = aws_iam_role.scheduler_invoke.arn
      }, contains(["api", "daily_sync", "match_polling"], each.key) ? {
      FOOTBALL_API_KEY = var.api_football_key
    } : {})
  }

  lifecycle {
    ignore_changes = [filename, source_code_hash]
  }
}
