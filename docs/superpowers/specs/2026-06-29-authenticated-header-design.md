# Authenticated Header Design

## Goal

Give authenticated users an obvious way to end their Cognito session without using browser storage or an incognito window.

## Design

Render one application-level header on every authenticated application page. The header displays `Bolão MaisBerlim` on the left and an outlined `Sair` button on the right. It appears on profile, prediction, and administration pages, but not on sign-in, rules, or privacy pages.

The button calls the existing `AuthContext.signOut` function. While sign-out is pending, the button is disabled to prevent duplicate requests. A successful sign-out updates the existing authentication state and returns the application to the sign-in page. If Cognito rejects the request, the button becomes available again and the current authenticated page remains visible.

## Scope

Keep the header in the application shell rather than duplicating it in individual feature pages. Do not add navigation, account details, or new authentication behavior.

## Verification

Add a focused frontend test that verifies the header is shown for an authenticated user and that clicking `Sair` calls the existing sign-out operation. Run the full frontend unit suite and lint checks.
