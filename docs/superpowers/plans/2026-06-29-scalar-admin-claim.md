# Scalar Admin Claim Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Authorize allowlisted administrators using a scalar `is_admin` access-token claim.

**Architecture:** Cognito invokes the existing pre-token Lambda with a V2 event. The Lambda adds `is_admin: "true"` for verified allowlisted emails, API Gateway forwards it as a string, and the .NET authorization policy requires that exact claim.

**Tech Stack:** Terraform, AWS Cognito, Node.js, ASP.NET Core, xUnit

---

### Task 1: Change the Cognito claim contract

**Files:**
- Modify: `infra/lambda-admin-claims/index.test.mjs`
- Modify: `infra/lambda-admin-claims/index.mjs`
- Modify: `infra/cognito.tf`

- [x] Replace group-oriented tests with tests that require `response.claimsAndScopeOverrideDetails.accessTokenGeneration.claimsToAddOrOverride.is_admin` to equal `"true"` for verified allowlisted emails and be absent otherwise.
- [x] Run `node --test infra/lambda-admin-claims/index.test.mjs` and confirm it fails because the V2 claim implementation is missing.
- [x] Replace `applyAdminGroup` with the minimal V2 scalar-claim implementation and set the Terraform trigger version to `V2_0`.
- [x] Run the Node tests again and confirm they pass.

### Task 2: Authorize the scalar backend claim

**Files:**
- Modify: `backend/tests/Bolao.Functions.Tests/Api/AdminEndpointTests.cs`
- Modify: `backend/tests/Bolao.Functions.Tests/Api/ParticipantEndpointTests.cs`
- Modify: `backend/src/Bolao.Functions/AppBootstrap.cs`
- Modify: `backend/src/Bolao.Functions/Auth/GatewayAuthenticationHandler.cs`

- [x] Change the test authentication seam to emit `is_admin` from `X-Test-Is-Admin`, and make the admin endpoint test send `true`.
- [x] Run the admin endpoint tests and confirm the positive admin test fails under the existing group policy.
- [x] Require `is_admin=true`, emit it for the E2E admin token, and remove only the special `cognito:groups` reconstruction code.
- [x] Run the admin endpoint tests again and confirm they pass.

### Task 3: Verify the complete change

**Files:**
- Verify all files above without committing them.

- [x] Run the Lambda tests, complete backend test suite, Terraform formatting/validation, frontend tests, and `git diff --check`.
- [x] Inspect `git diff` and `git status --short` to confirm only scoped, uncommitted changes are present.
