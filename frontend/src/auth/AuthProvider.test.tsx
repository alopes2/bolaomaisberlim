import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { useAuth } from './auth-context'
import { AuthProvider } from './AuthProvider'

function AuthStatus() {
  return <p>{useAuth().status}</p>
}

describe('AuthProvider', () => {
  it('restores an authenticated session from the access token', async () => {
    const auth = {
      signIn: vi.fn(),
      signOut: vi.fn(),
      accessToken: vi.fn().mockResolvedValue('token'),
    }

    render(
      <AuthProvider client={auth}>
        <AuthStatus />
      </AuthProvider>,
    )

    expect(await screen.findByText('authenticated')).toBeVisible()
  })
})
