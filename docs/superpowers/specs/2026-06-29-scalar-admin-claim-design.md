# Scalar Admin Claim Design

## Goal

Authorize allowlisted administrators without depending on Cognito group membership or API Gateway's serialization of the array-valued `cognito:groups` claim.

## Authentication flow

1. Google authenticates the user through the Cognito hosted UI.
2. Before issuing tokens, Cognito invokes the existing pre-token-generation Lambda with a `V2_0` event.
3. The Lambda normalizes the verified email and compares it with `ADMIN_EMAILS`.
4. For an allowlisted, verified email, the Lambda adds the string claim `is_admin: "true"` to the access token. It omits the claim for other users.
5. The frontend continues sending the Cognito access token as `Authorization: Bearer <token>`.
6. API Gateway validates the token and forwards the scalar claim to the API Lambda in `requestContext.authorizer.jwt.claims`.
7. The API authorization policy requires the exact claim `is_admin=true` for `/admin` routes.

## Cognito event contract

The pre-token Lambda receives the existing user attributes plus V2 access-token fields:

```json
{
  "version": "2",
  "triggerSource": "TokenGeneration_HostedAuth",
  "request": {
    "userAttributes": {
      "email": "admin@example.com",
      "email_verified": "true"
    },
    "scopes": ["openid", "email", "profile"],
    "groupConfiguration": {}
  },
  "response": {}
}
```

For an administrator, it returns the event with this response:

```json
{
  "claimsAndScopeOverrideDetails": {
    "accessTokenGeneration": {
      "claimsToAddOrOverride": {
        "is_admin": "true"
      }
    }
  }
}
```

The claim is deliberately a string because API Gateway exposes JWT claims to the .NET Lambda as string values.

## Scope

- Change the Cognito trigger from `V1_0` to `V2_0`.
- Replace dynamic `admins` group injection with the scalar access-token claim.
- Change the backend admin policy to require `is_admin=true`.
- Remove the no-longer-needed special parsing of `cognito:groups`.
- Keep `ADMIN_EMAILS`, Google sign-in, frontend token handling, and public/participant authorization unchanged.

## Tests

- Pre-token Lambda adds `is_admin: "true"` only for verified allowlisted emails.
- Non-allowlisted and unverified users receive no admin claim.
- Backend admin routes accept `is_admin=true` and reject authenticated users without it.
- Existing infrastructure validation, backend tests, and frontend tests remain green.
