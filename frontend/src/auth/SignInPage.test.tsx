import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { SignInPage } from './SignInPage'

describe('SignInPage', () => {
  it('starts Google sign-in', async () => {
    const user = userEvent.setup()
    const onAuthenticated = vi.fn()
    const auth = {
      signIn: vi.fn().mockResolvedValue(undefined),
      signOut: vi.fn().mockResolvedValue(undefined),
      accessToken: vi.fn().mockResolvedValue(null),
    }

    render(<SignInPage auth={auth} onAuthenticated={onAuthenticated} />)

    await user.click(screen.getByRole('button', { name: /entrar com google/i }))

    expect(auth.signIn).toHaveBeenCalledOnce()
    expect(onAuthenticated).toHaveBeenCalledOnce()
  })

  it('shows a Google sign-in failure', async () => {
    const user = userEvent.setup()
    const auth = {
      signIn: vi.fn().mockRejectedValue(new Error('Google indisponível.')),
      signOut: vi.fn().mockResolvedValue(undefined),
      accessToken: vi.fn().mockResolvedValue(null),
    }

    render(<SignInPage auth={auth} onAuthenticated={vi.fn()} />)

    await user.click(screen.getByRole('button', { name: /entrar com google/i }))

    expect(await screen.findByText('Google indisponível.')).toBeVisible()
  })
})
