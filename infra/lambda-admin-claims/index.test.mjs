import assert from 'node:assert/strict'
import test from 'node:test'

import { applyAdminClaim, handler } from './index.mjs'

function event(email, emailVerified = 'true', groups = ['eu-central-1_pool_Google']) {
  return {
    request: {
      userAttributes: { email, email_verified: emailVerified },
      groupConfiguration: { groupsToOverride: groups },
    },
    response: {},
  }
}

function accessTokenClaims(result) {
  return result.response.claimsAndScopeOverrideDetails
    .accessTokenGeneration.claimsToAddOrOverride
}

test('adds is_admin for a normalized verified allowlisted email', () => {
  const result = applyAdminClaim(
    event(' Admin@Example.com '),
    ['admin@example.com'],
  )

  assert.equal(accessTokenClaims(result).is_admin, 'true')
})

test('does not add is_admin for an unverified email', () => {
  const result = applyAdminClaim(
    event('admin@example.com', 'false'),
    ['admin@example.com'],
  )

  assert.equal(Object.hasOwn(accessTokenClaims(result), 'is_admin'), false)
})

test('does not add is_admin for a non-allowlisted email', () => {
  const result = applyAdminClaim(
    event('user@example.com'),
    ['admin@example.com'],
  )

  assert.equal(Object.hasOwn(accessTokenClaims(result), 'is_admin'), false)
})

test('reads the admin allowlist from the Lambda environment', async () => {
  process.env.ADMIN_EMAILS = '["admin@example.com"]'

  const result = await handler(event('admin@example.com'))

  assert.equal(accessTokenClaims(result).is_admin, 'true')
})
