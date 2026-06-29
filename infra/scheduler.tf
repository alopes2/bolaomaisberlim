resource "aws_scheduler_schedule_group" "matches" {
  name = "${local.name_prefix}-matches"
}

resource "aws_iam_role" "scheduler_invoke" {
  name = "${local.name_prefix}-scheduler-invoke"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Principal = {
        Service = "scheduler.amazonaws.com"
      }
      Action = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy" "scheduler_invoke" {
  name = "invoke-jobs"
  role = aws_iam_role.scheduler_invoke.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = ["lambda:InvokeFunction"]
      Resource = [
        aws_lambda_function.this["daily_sync"].arn,
        aws_lambda_function.this["match_polling"].arn,
        aws_lambda_function.this["retention"].arn
      ]
    }]
  })
}

resource "aws_iam_role_policy" "polling_scheduler_management" {
  for_each = toset(["api", "daily_sync", "match_polling"])

  name = "manage-match-schedules"
  role = aws_iam_role.lambda[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "scheduler:CreateSchedule",
          "scheduler:DeleteSchedule",
          "scheduler:GetSchedule",
          "scheduler:UpdateSchedule"
        ]
        Resource = "arn:${data.aws_partition.current.partition}:scheduler:${var.aws_region}:${data.aws_caller_identity.current.account_id}:schedule/${aws_scheduler_schedule_group.matches.name}/*"
      },
      {
        Effect   = "Allow"
        Action   = ["iam:PassRole"]
        Resource = aws_iam_role.scheduler_invoke.arn
      }
    ]
  })
}

resource "aws_scheduler_schedule" "daily_sync" {
  name                         = "${local.name_prefix}-daily-sync"
  group_name                   = aws_scheduler_schedule_group.matches.name
  schedule_expression          = "cron(0 6 * * ? *)"
  schedule_expression_timezone = "Europe/Berlin"

  flexible_time_window {
    mode = "OFF"
  }

  target {
    arn      = aws_lambda_function.this["daily_sync"].arn
    role_arn = aws_iam_role.scheduler_invoke.arn
    input    = jsonencode({ source = "daily" })
  }
}

resource "aws_scheduler_schedule" "retention" {
  name                         = "${local.name_prefix}-retention"
  group_name                   = aws_scheduler_schedule_group.matches.name
  schedule_expression          = "cron(30 5 * * ? *)"
  schedule_expression_timezone = "Europe/Berlin"

  flexible_time_window {
    mode = "OFF"
  }

  target {
    arn      = aws_lambda_function.this["retention"].arn
    role_arn = aws_iam_role.scheduler_invoke.arn
    input    = jsonencode({ source = "retention" })
  }
}
