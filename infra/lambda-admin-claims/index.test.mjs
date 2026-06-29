import assert from 'node:assert/strict'
import test from 'node:test'

import { applyAdminGroup, handler } from './index.mjs'

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
  const result = applyAdminGroup(
    event('admin@example.com', 'false'),
    ['admin@example.com'],
  )

  assert.deepEqual(
    result.response.claimsOverrideDetails.groupOverrideDetails.groupsToOverride,
    ['eu-central-1_pool_Google'],
  )
})

test('does not add admins for a non-allowlisted email', () => {
  const result = applyAdminGroup(
    event('user@example.com'),
    ['admin@example.com'],
  )

  assert.deepEqual(
    result.response.claimsOverrideDetails.groupOverrideDetails.groupsToOverride,
    ['eu-central-1_pool_Google'],
  )
})

test('reads the admin allowlist from the Lambda environment', async () => {
  process.env.ADMIN_EMAILS = '["admin@example.com"]'

  const result = await handler(event('admin@example.com'))

  assert.equal(
    result.response.claimsOverrideDetails.groupOverrideDetails.groupsToOverride.includes('admins'),
    true,
  )
})
