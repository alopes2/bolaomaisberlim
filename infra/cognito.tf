resource "aws_cognito_user_pool" "main" {
  name                     = "${local.name_prefix}-users"
  user_pool_tier           = "ESSENTIALS"
  username_attributes      = ["email"]
  auto_verified_attributes = ["email"]
  mfa_configuration        = "OFF"

  sign_in_policy {
    allowed_first_auth_factors = ["EMAIL_OTP"]
  }

  username_configuration {
    case_sensitive = false
  }

  email_configuration {
    email_sending_account  = var.ses_identity_arn == null ? "COGNITO_DEFAULT" : "DEVELOPER"
    source_arn             = var.ses_identity_arn
    from_email_address     = var.ses_from_email
    reply_to_email_address = var.ses_from_email
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
