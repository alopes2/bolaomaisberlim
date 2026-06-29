terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = local.common_tags
  }
}

data "aws_caller_identity" "current" {}
data "aws_partition" "current" {}

locals {
  name_prefix      = "${var.project_name}-${var.environment}"
  ses_identity_arn = var.ses_identity_arn != null && trimspace(var.ses_identity_arn) != "" ? var.ses_identity_arn : null
  ses_from_email   = var.ses_from_email != null && trimspace(var.ses_from_email) != "" ? var.ses_from_email : null
  common_tags = {
    application = var.project_name
    environment = var.environment
  }
}
