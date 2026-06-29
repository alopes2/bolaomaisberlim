resource "aws_cognito_user_pool" "main" {
  name                     = "${local.name_prefix}-users"
  user_pool_tier           = "ESSENTIALS"
  username_attributes      = ["email"]
  auto_verified_attributes = ["email"]
  mfa_configuration        = "OFF"

  sign_in_policy {
    allowed_first_auth_factors = ["EMAIL_OTP", "PASSWORD"]
  }

  username_configuration {
    case_sensitive = false
  }

  email_configuration {
    email_sending_account  = local.ses_identity_arn == null ? "COGNITO_DEFAULT" : "DEVELOPER"
    source_arn             = local.ses_identity_arn
    from_email_address     = local.ses_from_email
    reply_to_email_address = local.ses_from_email
  }
}

resource "aws_cognito_user_pool_client" "web" {
  name         = "${local.name_prefix}-web"
  user_pool_id = aws_cognito_user_pool.main.id

  generate_secret = false
  explicit_auth_flows = [
    "ALLOW_USER_AUTH",
    "ALLOW_REFRESH_TOKEN_AUTH"
  ]
  prevent_user_existence_errors = "ENABLED"
}

resource "aws_cognito_user_group" "admins" {
  name         = "admins"
  user_pool_id = aws_cognito_user_pool.main.id
  description  = "MaisBerlim bolao administrators"
}

resource "aws_cognito_user" "admins" {
  for_each = var.admin_emails

  user_pool_id = aws_cognito_user_pool.main.id
  username     = each.value

  attributes = {
    email          = each.value
    email_verified = "true"
  }

  message_action = "SUPPRESS"
}

resource "aws_cognito_user_in_group" "admins" {
  for_each = var.admin_emails

  user_pool_id = aws_cognito_user_pool.main.id
  group_name   = aws_cognito_user_group.admins.name
  username     = aws_cognito_user.admins[each.key].username
}
