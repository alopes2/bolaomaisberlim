# Cognito Google Sign-In Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Cognito email-code login with Google Sign-In while preserving Cognito JWTs, API Gateway authorization, and the Terraform admin-email allowlist.

**Architecture:** Cognito remains the OAuth broker and token issuer. Terraform adds a Cognito domain, Google identity provider, OAuth app-client settings, and a small Node.js pre-token Lambda that adds `admins` to verified allowlisted users. Amplify redirects the browser through Cognito to Google and restores the existing Cognito session on return.

**Tech Stack:** Terraform 1.x, AWS provider 6.x, Cognito User Pools, AWS Lambda Node.js 22, React 19, Amplify Auth 6, Vitest, Playwright, GitHub Actions.

---

## File Structure

- Create `infra/lambda-admin-claims/index.mjs`: isolated Cognito pre-token handler and pure allowlist logic.
- Create `infra/lambda-admin-claims/index.test.mjs`: Node test coverage for group preservation and admin matching.
- Modify `infra/cognito.tf`: Google federation, OAuth client, domain, trigger wiring, and removal of Terraform-created local admin users.
- Modify `infra/lambda.tf`: minimal execution role, logging policy, archive, function, and Cognito invoke permission for the trigger.
- Modify `infra/variables.tf`: explicit OAuth, domain, callback, and logout inputs.
- Modify `infra/outputs.tf`: Cognito domain and Google redirect URI handoff outputs.
- Modify `.github/workflows/infra.yml`: pass OAuth Terraform variables and the Google secret.
- Modify `.github/workflows/frontend.yml`: provide the Cognito domain to Vite.
- Replace `frontend/src/auth/cognito.ts`: Google redirect adapter while retaining the existing `AuthClient` boundary.
- Replace `frontend/src/auth/cognito.test.ts`: unit tests for redirect, tokens, and sign-out.
- Replace `frontend/src/auth/SignInPage.tsx`: single Google sign-in action.
- Replace `frontend/src/auth/SignInPage.test.tsx`: sign-in success and error tests.
- Modify `frontend/e2e/admin-result-flow.spec.ts` and `frontend/e2e/prediction-flow.spec.ts`: remove email-code interactions.
- Modify `README.md`: deployment inputs, Google Console setup, admin behavior, and removal of obsolete Cognito-user instructions.

The existing uncommitted passwordless-registration spec, plan, implementation, and tests are superseded by this work. Do not commit during execution; the repository owner will review and commit the final diff.

### Task 1: Admin Claim Trigger

**Files:**
- Create: `infra/lambda-admin-claims/index.test.mjs`
- Create: `infra/lambda-admin-claims/index.mjs`
- Modify: `infra/lambda.tf`
- Test: `infra/lambda-admin-claims/index.test.mjs`

- [x] **Step 1: Write the failing tests**

Create tests with `node:test` that call `applyAdminGroup(event, adminEmails)` and assert:

```js
import assert from 'node:assert/strict'
import test from 'node:test'

import { applyAdminGroup } from './index.mjs'

function event(email, emailVerified = 'true', groups = ['eu-central-1_pool_Google']) {
  return {
    request: {
      userAttributes: { email, email_verified: emailVerified },
      groupConfiguration: { groupsToOverride: groups },
    },
    response: {},
  }
}

test('adds admins for a normalized verified allowlisted email', () => {
  const result = applyAdminGroup(
    event(' Admin@Example.com '),
    ['admin@example.com'],
  )
  assert.deepEqual(
    result.response.claimsOverrideDetails.groupOverrideDetails.groupsToOverride,
    ['eu-central-1_pool_Google', 'admins'],
  )
})

test('does not add admins for an unverified email', () => {
  const result = applyAdminGroup(event('admin@example.com', 'false'), ['admin@example.com'])
  assert.deepEqual(
    result.response.claimsOverrideDetails.groupOverrideDetails.groupsToOverride,
    ['eu-central-1_pool_Google'],
  )
})

test('does not add admins for a non-allowlisted email', () => {
  const result = applyAdminGroup(event('user@example.com'), ['admin@example.com'])
  assert.deepEqual(
    result.response.claimsOverrideDetails.groupOverrideDetails.groupsToOverride,
    ['eu-central-1_pool_Google'],
  )
})
```

- [x] **Step 2: Run the tests and verify the red state**

Run: `node --test infra/lambda-admin-claims/index.test.mjs`

Expected: FAIL because `index.mjs` does not exist.

- [x] **Step 3: Implement the pure function and Lambda handler**

Implement only normalization, verified-email matching, preservation of existing groups, and the Cognito V1 response shape:

```js
function normalize(email) {
  return email.trim().toLowerCase()
}

export function applyAdminGroup(event, adminEmails) {
  const attributes = event.request?.userAttributes ?? {}
  const groups = event.request?.groupConfiguration?.groupsToOverride ?? []
  const allowlist = new Set(adminEmails.map(normalize))
  const isAdmin = attributes.email_verified === 'true'
    && typeof attributes.email === 'string'
    && allowlist.has(normalize(attributes.email))
  const groupsToOverride = isAdmin
    ? [...new Set([...groups, 'admins'])]
    : groups.filter((group) => group !== 'admins')

  return {
    ...event,
    response: {
      ...(event.response ?? {}),
      claimsOverrideDetails: {
        groupOverrideDetails: {
          groupsToOverride,
          iamRolesToOverride: [],
          preferredRole: null,
        },
      },
    },
  }
}

export async function handler(event) {
  const adminEmails = JSON.parse(process.env.ADMIN_EMAILS ?? '[]')
  return applyAdminGroup(event, adminEmails)
}
```

- [x] **Step 4: Run the focused tests**

Run: `node --test infra/lambda-admin-claims/index.test.mjs`

Expected: 3 tests pass.

- [x] **Step 5: Provision the isolated trigger Lambda**

Add a source-file archive, Lambda execution role, logging policy, Node.js 22 function, and Cognito invoke permission to `infra/lambda.tf`. The function must receive only this environment value:

```hcl
environment {
  variables = {
    ADMIN_EMAILS = jsonencode(sort([
      for email in var.admin_emails : lower(trimspace(email))
    ]))
  }
}
```

Use `infra/lambda-admin-claims/index.mjs` as the archive source, `index.handler` as the handler, and do not add the function to `local.lambda_functions`; infrastructure deploys this function directly.

- [x] **Step 6: Checkpoint the focused diff**

Run: `git diff --check -- infra/lambda-admin-claims infra/lambda.tf`

Expected: exit 0.

### Task 2: Cognito Google Federation

**Files:**
- Modify: `infra/variables.tf`
- Modify: `infra/cognito.tf`
- Modify: `infra/outputs.tf`
- Modify: `.github/workflows/infra.yml`

- [x] **Step 1: Add validated Terraform inputs**

Add:

```hcl
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
```

- [x] **Step 2: Reconfigure the user pool and app client**

In `infra/cognito.tf`:

- Set Cognito email delivery to `COGNITO_DEFAULT` without a source or FROM address.
- Wire the pre-token Lambda with `lambda_version = "V1_0"`.
- Add `aws_cognito_user_pool_domain`.
- Add `aws_cognito_identity_provider` with Google `client_id`, `client_secret`, `authorize_scopes = "openid email profile"`, and mappings for `email`, `email_verified`, `given_name`, and `family_name`.
- Enable the app client's authorization-code flow, `openid/email/profile` scopes, callback/logout lists, and `supported_identity_providers = ["Google"]`.
- Preserve `ALLOW_REFRESH_TOKEN_AUTH`.
- Remove `aws_cognito_user.admins` and `aws_cognito_user_in_group.admins`; preserve the `admins` group.

The app client must depend on the Google identity provider so Cognito does not reject `supported_identity_providers` during creation.

- [x] **Step 3: Add handoff outputs**

Add:

```hcl
output "cognito_domain" {
  value = "${aws_cognito_user_pool_domain.main.domain}.auth.${var.aws_region}.amazoncognito.com"
}

output "google_oauth_redirect_uri" {
  value = "https://${aws_cognito_user_pool_domain.main.domain}.auth.${var.aws_region}.amazoncognito.com/oauth2/idpresponse"
}
```

- [x] **Step 4: Pass the inputs in both infrastructure jobs**

Add these environment mappings to both `plan` and `apply` in `.github/workflows/infra.yml`:

```yaml
TF_VAR_cognito_domain_prefix: ${{ vars.COGNITO_DOMAIN_PREFIX }}
TF_VAR_cognito_callback_urls: ${{ vars.COGNITO_CALLBACK_URLS }}
TF_VAR_cognito_logout_urls: ${{ vars.COGNITO_LOGOUT_URLS }}
TF_VAR_google_client_id: ${{ vars.GOOGLE_CLIENT_ID }}
TF_VAR_google_client_secret: ${{ secrets.GOOGLE_CLIENT_SECRET }}
```

- [x] **Step 5: Validate infrastructure**

Run:

```bash
terraform fmt -recursive infra
terraform -chdir=infra init -backend=false -input=false
terraform -chdir=infra validate
ruby -e "require 'yaml'; YAML.parse_file('.github/workflows/infra.yml')"
git diff --check
```

Expected: formatting produces no follow-up diff, Terraform reports success, YAML parses, and diff check exits 0.

### Task 3: Frontend Google Redirect Adapter

**Files:**
- Replace: `frontend/src/auth/cognito.test.ts`
- Replace: `frontend/src/auth/cognito.ts`
- Modify: `.github/workflows/frontend.yml`

- [x] **Step 1: Write failing adapter tests**

Mock `aws-amplify/auth` and assert that:

```ts
await new CognitoAuthClient().signIn()
expect(signInWithRedirect).toHaveBeenCalledWith({ provider: 'Google' })

await new CognitoAuthClient().signOut()
expect(amplifySignOut).toHaveBeenCalledOnce()

expect(await new CognitoAuthClient().accessToken()).toBe('token')
```

Retain the unauthenticated-session case: `UserUnAuthenticatedException` returns `null`; other errors are rethrown.

- [x] **Step 2: Run the focused test and verify the red state**

Run: `npm --prefix frontend run test:run -- src/auth/cognito.test.ts`

Expected: FAIL because the current interface exposes `start`/`confirm`, not `signIn`.

- [x] **Step 3: Replace the auth adapter**

Use this interface and production behavior:

```ts
export interface AuthClient {
  signIn(): Promise<void>
  signOut(): Promise<void>
  accessToken(): Promise<string | null>
}

export class CognitoAuthClient implements AuthClient {
  signIn() {
    return signInWithRedirect({ provider: 'Google' })
  }

  signOut() {
    return signOut()
  }

  async accessToken() {
    try {
      const session = await fetchAuthSession()
      return session.tokens?.accessToken?.toString() ?? null
    } catch (error) {
      if (error instanceof Error && error.name === 'UserUnAuthenticatedException') return null
      throw error
    }
  }
}
```

Configure Amplify OAuth with the required `VITE_COGNITO_DOMAIN`, `responseType: 'code'`, `scopes: ['openid', 'email', 'profile']`, and `redirectSignIn`/`redirectSignOut` set to ``[`${window.location.origin}/`]``. Preserve the E2E adapter; its `signIn()` stores `e2e-user`.

- [x] **Step 4: Add the frontend deployment value**

Add to `.github/workflows/frontend.yml`:

```yaml
VITE_COGNITO_DOMAIN: ${{ vars.VITE_COGNITO_DOMAIN }}
```

- [x] **Step 5: Run focused adapter tests**

Run: `npm --prefix frontend run test:run -- src/auth/cognito.test.ts`

Expected: all adapter tests pass.

### Task 4: Google-Only Sign-In Page and E2E Flow

**Files:**
- Replace: `frontend/src/auth/SignInPage.test.tsx`
- Replace: `frontend/src/auth/SignInPage.tsx`
- Modify: `frontend/e2e/admin-result-flow.spec.ts`
- Modify: `frontend/e2e/prediction-flow.spec.ts`

- [x] **Step 1: Write failing page tests**

Test one successful redirect initiation and one visible error:

```ts
it('starts Google sign-in', async () => {
  const auth = {
    signIn: vi.fn().mockResolvedValue(undefined),
    signOut: vi.fn().mockResolvedValue(undefined),
    accessToken: vi.fn().mockResolvedValue(null),
  }
  render(<SignInPage auth={auth} onAuthenticated={vi.fn()} />)
  await userEvent.click(screen.getByRole('button', { name: /entrar com google/i }))
  expect(auth.signIn).toHaveBeenCalledOnce()
})

it('shows a Google sign-in failure', async () => {
  const auth = {
    signIn: vi.fn().mockRejectedValue(new Error('Google indisponível.')),
    signOut: vi.fn().mockResolvedValue(undefined),
    accessToken: vi.fn().mockResolvedValue(null),
  }
  render(<SignInPage auth={auth} onAuthenticated={vi.fn()} />)
  await userEvent.click(screen.getByRole('button', { name: /entrar com google/i }))
  expect(await screen.findByText('Google indisponível.')).toBeVisible()
})
```

- [x] **Step 2: Run the page test and verify the red state**

Run: `npm --prefix frontend run test:run -- src/auth/SignInPage.test.tsx`

Expected: FAIL because the current page renders email and OTP forms.

- [x] **Step 3: Replace the page with one action**

Retain the existing card shell. Render the description `Entre com sua conta Google para continuar.` and one submit button labeled `Entrar com Google`. On submit, call `auth.signIn()` and then `onAuthenticated()` so the deterministic E2E adapter refreshes immediately. Show thrown errors through the existing `FieldError` pattern and disable the button while pending.

- [x] **Step 4: Update E2E authentication**

For the participant flow, replace the email/code helper body with:

```ts
await page.goto('/')
await page.getByRole('button', { name: 'Entrar com Google' }).click()
```

For the admin flow, seed the existing test-only admin token before opening the admin page:

```ts
await page.goto('/')
await page.evaluate(() => localStorage.setItem('bolao-e2e-token', 'e2e-admin'))
await page.goto('/admin?matchId=match-e2e')
```

- [x] **Step 5: Run page and E2E tests**

Run:

```bash
npm --prefix frontend run test:run -- src/auth/SignInPage.test.tsx
npm --prefix frontend run test:e2e
```

Expected: focused tests pass and both Playwright scenarios pass.

### Task 5: Documentation and Final Verification

**Files:**
- Modify: `README.md`
- Delete: `docs/superpowers/plans/2026-06-29-passwordless-self-registration.md`
- Delete: `docs/superpowers/specs/2026-06-29-passwordless-self-registration-design.md`

- [x] **Step 1: Remove superseded passwordless documents**

Delete only the two untracked passwordless documents. Preserve the approved Google design and this plan.

- [x] **Step 2: Update deployment documentation**

Update the GitHub Actions tables so they state:

- `ADMIN_EMAILS` is a GitHub variable containing a JSON array.
- `COGNITO_DOMAIN_PREFIX`, `COGNITO_CALLBACK_URLS`, `COGNITO_LOGOUT_URLS`, `GOOGLE_CLIENT_ID`, and `VITE_COGNITO_DOMAIN` are variables.
- `GOOGLE_CLIENT_SECRET` is a secret.
- Terraform no longer creates admin users; a verified Google email receives admin claims at login.
- Google Console must configure the OAuth consent screen and add Terraform's `google_oauth_redirect_uri` output to the web client's authorized redirect URIs.
- Cognito does not use custom SES for login; SES inputs now control winner notifications only.

- [x] **Step 3: Run the complete verification suite**

Run:

```bash
node --test infra/lambda-admin-claims/index.test.mjs
terraform fmt -check -recursive infra
terraform -chdir=infra init -backend=false -input=false
terraform -chdir=infra validate
ruby -e "require 'yaml'; Dir['.github/workflows/*.yml'].each { |file| YAML.parse_file(file) }"
npm --prefix frontend run test:run
npm --prefix frontend run lint
npm --prefix frontend run build
npm --prefix frontend run test:e2e
git diff --check
```

Expected: all tests and validation pass; lint has no errors; build succeeds; both E2E scenarios pass; diff check exits 0.

- [x] **Step 4: Review the final scope and secrets**

Run:

```bash
git status --short
git diff --stat
rg -n "GOOGLE_CLIENT_SECRET|client_secret" . -g '!docs/superpowers/**' -g '!infra/.terraform/**' -g '!frontend/node_modules/**'
```

Expected: only intended files are changed; references to the secret are variable/workflow expressions, never a literal credential.

- [x] **Step 5: Prepare the owner handoff**

Report:

1. Verification results and any warnings.
2. The Google Console steps: create/select project, configure external consent, create Web OAuth client, use the Terraform redirect URI.
3. Exact GitHub variables and secrets, including JSON formatting for URL lists.
4. Terraform outputs to copy into frontend variables.
5. The destructive plan consequence: Terraform removes the old local Cognito admin users, and Google users receive new Cognito `sub` values.

Do not commit or push.
