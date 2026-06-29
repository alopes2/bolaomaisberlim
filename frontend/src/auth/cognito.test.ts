import { beforeEach, describe, expect, it, vi } from 'vitest'

const authMocks = vi.hoisted(() => ({
  fetchAuthSession: vi.fn(),
  signInWithRedirect: vi.fn(),
  signOut: vi.fn(),
}))

vi.mock('aws-amplify/auth', () => authMocks)

import { CognitoAuthClient } from './cognito'

describe('CognitoAuthClient', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('starts Google sign-in', async () => {
    authMocks.signInWithRedirect.mockResolvedValueOnce(undefined)
    const auth = new CognitoAuthClient()

    await auth.signIn()

    expect(authMocks.signInWithRedirect).toHaveBeenCalledWith({
      provider: 'Google',
    })
  })

  it('signs out through Amplify', async () => {
    authMocks.signOut.mockResolvedValueOnce(undefined)
    const auth = new CognitoAuthClient()

    await auth.signOut()

    expect(authMocks.signOut).toHaveBeenCalledOnce()
  })

  it('returns the Cognito access token', async () => {
    authMocks.fetchAuthSession.mockResolvedValueOnce({
      tokens: { accessToken: { toString: () => 'token' } },
    })
    const auth = new CognitoAuthClient()

    await expect(auth.accessToken()).resolves.toBe('token')
  })

  it('returns null when there is no authenticated user', async () => {
    const error = new Error('not signed in')
    error.name = 'UserUnAuthenticatedException'
    authMocks.fetchAuthSession.mockRejectedValueOnce(error)
    const auth = new CognitoAuthClient()

    await expect(auth.accessToken()).resolves.toBeNull()
  })

  it('rethrows unexpected session failures', async () => {
    authMocks.fetchAuthSession.mockRejectedValueOnce(new Error('network'))
    const auth = new CognitoAuthClient()

    await expect(auth.accessToken()).rejects.toThrow('network')
  })
})
