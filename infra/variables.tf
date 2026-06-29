variable "aws_region" {
  description = "AWS region for regional resources."
  type        = string
  default     = "eu-central-1"
}

variable "project_name" {
  description = "Stable project prefix used in resource names."
  type        = string
  default     = "bolaomaisberlim"
}

variable "environment" {
  description = "Deployment environment name."
  type        = string
  default     = "dev"
}

variable "api_football_key" {
  description = "API-Football key injected into the Lambdas that call the provider."
  type        = string
  sensitive   = true
}

variable "admin_emails" {
  description = "Verified Google email addresses that receive Cognito administrator claims."
  type        = set(string)
}

variable "cognito_domain_prefix" {
  description = "Globally unique Cognito managed-login domain prefix."
  type        = string
}

variable "cognito_callback_urls" {
  description = "Allowed OAuth callback URLs for the Cognito web client."
  type        = set(string)

  validation {
    condition     = length(var.cognito_callback_urls) > 0
    error_message = "cognito_callback_urls must contain at least one URL."
  }
}

variable "cognito_logout_urls" {
  description = "Allowed post-logout URLs for the Cognito web client."
  type        = set(string)

  validation {
    condition     = length(var.cognito_logout_urls) > 0
    error_message = "cognito_logout_urls must contain at least one URL."
  }
}

variable "google_client_id" {
  description = "Google OAuth web client ID used by Cognito."
  type        = string
}

variable "google_client_secret" {
  description = "Google OAuth web client secret used by Cognito."
  type        = string
  sensitive   = true
}

variable "ses_identity_arn" {
  description = "Verified SES identity ARN for winner notifications; null disables notification email."
  type        = string
  default     = null
}

variable "ses_from_email" {
  description = "Winner-notification From address used with ses_identity_arn."
  type        = string
  default     = null

  validation {
    condition     = (var.ses_identity_arn == null) == (var.ses_from_email == null)
    error_message = "ses_identity_arn and ses_from_email must be set together."
  }
}
