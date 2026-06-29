import '@testing-library/jest-dom/vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import type { ApiClient } from '@/api/client'
import { AuthContext, type AuthContextValue } from '@/auth/auth-context'

import { App } from './App'

function renderAuthenticatedApp(signOut: AuthContextValue['signOut']) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  const auth: AuthContextValue = {
    client: {
      signIn: vi.fn(),
      signOut: vi.fn(),
      accessToken: vi.fn().mockResolvedValue('token'),
    },
    status: 'authenticated',
    refresh: vi.fn(),
    signOut,
  }

  render(
    <AuthContext.Provider value={auth}>
      <QueryClientProvider client={queryClient}>
        <App api={{} as ApiClient} />
      </QueryClientProvider>
    </AuthContext.Provider>,
  )
}

describe('App', () => {
  afterEach(() => window.history.replaceState({}, '', '/'))

  it('shows the authenticated header and signs out', async () => {
    const user = userEvent.setup()
    const signOut = vi.fn().mockResolvedValue(undefined)
    window.history.replaceState({}, '', '/admin')

    renderAuthenticatedApp(signOut)

    expect(screen.getByText('Bolão MaisBerlim')).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Sair' }))
    expect(signOut).toHaveBeenCalledOnce()
  })
})
