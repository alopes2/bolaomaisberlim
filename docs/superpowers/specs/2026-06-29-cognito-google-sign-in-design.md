# Cognito Google Sign-In Design

## Goal

Replace email-code authentication with Google Sign-In while retaining Cognito as the application's token issuer. Login must not depend on Cognito or SES delivering email.

## Scope

- Google is the only identity provider exposed by the web app.
- Cognito continues to issue the JWTs accepted by API Gateway.
- Terraform manages the AWS-side Google federation configuration.
- A one-time Google Console setup supplies the OAuth client ID and secret.
- The existing `admin_emails` allowlist determines administrator access.
- Existing SES configuration remains available for winner notifications, but Cognito no longer uses the custom SES identity.

Email/password login, account linking, and migration of existing Cognito identities are out of scope.

## Infrastructure

Terraform will add a Cognito domain and a Google identity provider. The Google provider maps `email`, `email_verified`, `given_name`, and `family_name` into the federated Cognito user.

The web app client will use the OAuth authorization-code flow with `openid`, `email`, and `profile` scopes. It will accept explicit callback and logout URL lists for production and local development and expose only Google as a supported identity provider.

Cognito's user-pool email configuration will return to `COGNITO_DEFAULT`. Because the app does not expose a local Cognito sign-up or email-code flow, Cognito will not send login email. The existing SES variables and Lambda permissions remain dedicated to winner notifications.

The Google OAuth client uses this redirect URI:

`https://<cognito-domain-prefix>.auth.eu-central-1.amazoncognito.com/oauth2/idpresponse`

The Google client secret is sensitive input. It will be stored in GitHub Actions as a secret and will necessarily be present in Terraform state because Cognito stores it in the identity-provider resource.

## Administrator Authorization

Terraform-created local Cognito admin users will be removed. Google creates the federated Cognito user only on first login, so Terraform cannot pre-create the correct federated username.

A Cognito pre-token-generation Lambda will compare the mapped, verified Google email against `admin_emails`. Matching users receive the existing `admins` group override in their Cognito tokens. Non-matching or unverified users receive no administrator group.

The backend and API Gateway continue using the existing `cognito:groups=admins` policy. The allowlist comparison normalizes email addresses by trimming whitespace and using case-insensitive comparison.

Existing users receive new federated Cognito identities and therefore new `sub` values. No identity or profile migration is included.

## Frontend

The authentication client will configure Amplify OAuth settings using the Cognito domain and the current browser origin as the redirect target. The sign-in page becomes a single `Entrar com Google` action that calls Amplify's Google redirect flow.

After the OAuth callback, Amplify restores the Cognito session. The existing auth provider and API client continue reading the Cognito access token. Sign-out clears the Cognito session and returns to the application.

The E2E authentication adapter remains local and deterministic; its admin and participant tokens continue exercising the existing authorization behavior without contacting Google.

## Error Handling

- Missing Cognito OAuth configuration fails at startup with a stable configuration error.
- Redirect initiation errors appear on the sign-in page.
- Authentication callback/session failures return the user to the signed-out state.
- Google OAuth cancellation leaves the user signed out and able to retry.

## Deployment Inputs

GitHub Actions will pass these additional Terraform values:

- `COGNITO_DOMAIN_PREFIX`
- `COGNITO_CALLBACK_URLS`
- `COGNITO_LOGOUT_URLS`
- `GOOGLE_CLIENT_ID`
- `GOOGLE_CLIENT_SECRET` as a GitHub secret

The frontend build will receive the Cognito domain plus callback and logout URLs derived from its deployed URL. Existing Cognito pool and client outputs remain in use.

## Verification

- Unit tests cover Google redirect initiation, session restoration, sign-out, and configuration errors.
- Unit tests cover admin allowlist normalization and rejection of unverified/non-admin emails.
- Terraform formatting and validation pass.
- Frontend unit tests, production build, and repository E2E tests pass.
- The generated workflow YAML parses successfully and contains no plaintext Google client secret.
- A real Google login remains a post-deployment smoke test because it requires the deployed Cognito callback URL and an interactive Google account.
