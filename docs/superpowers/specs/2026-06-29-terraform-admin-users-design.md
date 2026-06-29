# Terraform-managed Cognito administrators

## Goal

Manage the initial Cognito administrator accounts and their `admins` group memberships through Terraform. Administrator emails are supplied by the protected GitHub `production` environment and are never committed to the repository.

## Configuration

Add a sensitive `admin_emails` Terraform variable with type `set(string)`. GitHub stores the value in an `ADMIN_EMAILS` environment secret as a JSON array, for example `["admin1@example.com", "admin2@example.com"]`. The infrastructure workflow exposes it to Terraform as `TF_VAR_admin_emails` in both plan and apply jobs.

## Resources

For each configured email, Terraform creates an `aws_cognito_user` in the existing user pool. The email is verified at creation, no password is assigned, and the invitation message is suppressed so the existing email-OTP flow performs authentication.

Terraform also creates an `aws_cognito_user_in_group` resource for each user, targeting the existing `admins` group. Using `nonsensitive(var.admin_emails)` as `for_each` gives stable identities independent of list order. This means individual emails can appear in resource addresses, plans, and state even though the GitHub input is protected.

## Lifecycle

Terraform owns these accounts. Adding an email creates a user and grants administrator access. Removing an email removes the group membership and deletes that Terraform-managed Cognito user. Existing users created outside Terraform are not adopted automatically and must be imported before adding their emails to the managed set.

## Verification

Run Terraform formatting and validation locally. Verify that the GitHub workflow parses the `ADMIN_EMAILS` JSON secret through `TF_VAR_admin_emails`, and review a Terraform plan before apply. No application code changes are required.
