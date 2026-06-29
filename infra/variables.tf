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

variable "ses_identity_arn" {
  description = "Verified SES identity ARN for production Cognito email; null uses Cognito default email."
  type        = string
  default     = null
}

variable "ses_from_email" {
  description = "From address used with ses_identity_arn."
  type        = string
  default     = null

  validation {
    condition     = (var.ses_identity_arn == null) == (var.ses_from_email == null)
    error_message = "ses_identity_arn and ses_from_email must be set together."
  }
}
