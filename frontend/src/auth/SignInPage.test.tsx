import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { SignInPage } from './SignInPage'

describe('SignInPage', () => {
  it('requests an email code and confirms it', async () => {
    const user = userEvent.setup()
    const auth = {
      start: vi.fn().mockResolvedValue(undefined),
      confirm: vi.fn().mockResolvedValue(undefined),
      signOut: vi.fn().mockResolvedValue(undefined),
      accessToken: vi.fn().mockResolvedValue(null),
    }

    render(<SignInPage auth={auth} onAuthenticated={vi.fn()} />)

    await user.type(screen.getByLabelText(/e-mail/i), 'ana@example.com')
    await user.click(screen.getByRole('button', { name: /enviar código/i }))

    expect(auth.start).toHaveBeenCalledWith('ana@example.com')

    const code = await screen.findByLabelText(/código/i)
    await user.type(code, '123456')
    await user.click(screen.getByRole('button', { name: /confirmar/i }))

    expect(auth.confirm).toHaveBeenCalledWith('123456')
  })
})
