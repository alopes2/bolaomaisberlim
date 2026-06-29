# Terraform-managed Cognito Administrators Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create all configured Cognito administrator users through Terraform and supply their email set securely from GitHub Actions.

**Architecture:** A sensitive Terraform `set(string)` drives native Cognito user and group-membership resources with stable email keys. The infrastructure workflow passes a protected JSON-array secret through Terraform's `TF_VAR_admin_emails` convention.

**Tech Stack:** Terraform 1.10+, HashiCorp AWS provider 6.x, AWS Cognito, GitHub Actions

---

### Task 1: Manage Cognito administrator users

**Files:**
- Modify: `infra/variables.tf`
- Modify: `infra/cognito.tf`
- Modify: `infra/terraform.tfvars.example`

- [x] **Step 1: Verify the resources are not already declared**

Run:

```bash
rg -q 'variable "admin_emails"' infra/variables.tf && \
rg -q 'resource "aws_cognito_user" "admins"' infra/cognito.tf && \
rg -q 'resource "aws_cognito_user_in_group" "admins"' infra/cognito.tf
```

Expected: exit 1 because the administrator resources do not exist yet.

- [x] **Step 2: Define the sensitive email set**

Append to `infra/variables.tf`:

```hcl
variable "admin_emails" {
  description = "Email addresses for Terraform-managed Cognito administrators."
  type        = set(string)
  sensitive   = true
}
```

- [x] **Step 3: Create users and group memberships**

Append to `infra/cognito.tf`:

```hcl
resource "aws_cognito_user" "admins" {
  for_each = nonsensitive(var.admin_emails)

  user_pool_id = aws_cognito_user_pool.main.id
  username     = each.value

  attributes = {
    email          = each.value
    email_verified = "true"
  }

  message_action = "SUPPRESS"
}

resource "aws_cognito_user_in_group" "admins" {
  for_each = nonsensitive(var.admin_emails)

  user_pool_id = aws_cognito_user_pool.main.id
  group_name   = aws_cognito_user_group.admins.name
  username     = aws_cognito_user.admins[each.key].username
}
```

- [x] **Step 4: Document the local input format**

Append to `infra/terraform.tfvars.example`:

```hcl
# Supply real administrator emails through TF_VAR_admin_emails as a JSON array.
# admin_emails = ["admin1@example.com", "admin2@example.com"]
```

- [x] **Step 5: Format and validate Terraform**

Run:

```bash
terraform fmt -check -recursive infra
terraform -chdir=infra init -backend=false -input=false
terraform -chdir=infra validate
```

Expected: formatting check exits 0 and Terraform reports `Success! The configuration is valid.`

### Task 2: Supply administrator emails in deployments

**Files:**
- Modify: `.github/workflows/infra.yml`
- Modify: `README.md`

- [x] **Step 1: Verify the workflow does not already supply the variable**

Run:

```bash
test "$(rg -c 'TF_VAR_admin_emails:' .github/workflows/infra.yml)" -eq 2
```

Expected: exit 1 because neither infrastructure job declares the variable.

- [x] **Step 2: Add the secret to plan and apply jobs**

Add this entry to the `env` block of both jobs in `.github/workflows/infra.yml`:

```yaml
TF_VAR_admin_emails: ${{ secrets.ADMIN_EMAILS }}
```

- [x] **Step 3: Document the protected secret**

Update `README.md` to require an `ADMIN_EMAILS` GitHub Environment secret containing a JSON array such as:

```json
["admin1@example.com", "admin2@example.com"]
```

State that removing an email from the set deletes the Terraform-managed Cognito account.

- [x] **Step 4: Run complete configuration verification**

Run:

```bash
terraform fmt -check -recursive infra
terraform -chdir=infra init -backend=false -input=false
terraform -chdir=infra validate
ruby -e "require 'yaml'; Dir['.github/workflows/*.yml'].each { |file| YAML.parse_file(file) }"
test "$(rg -c 'TF_VAR_admin_emails:' .github/workflows/infra.yml)" -eq 2
git diff --check
```

Expected: every command exits 0 with no formatting, Terraform, YAML, wiring, or whitespace errors.

The repository owner commits the completed changes according to the project git policy.
