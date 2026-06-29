import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { ProfilePage } from './ProfilePage'

describe('ProfilePage', () => {
  it('saves the private name and displays the public label', async () => {
    const user = userEvent.setup()
    const api = {
      saveProfile: vi.fn().mockResolvedValue({
        publicName: 'Ana S.',
        suffix: '#2',
      }),
    }
    const onCompleted = vi.fn()

    render(<ProfilePage api={api} onCompleted={onCompleted} />)

    await user.type(screen.getByLabelText(/^nome$/i), 'Ana')
    await user.type(screen.getByLabelText(/sobrenome/i), 'Silva')
    await user.click(screen.getByRole('button', { name: /salvar perfil/i }))

    expect(api.saveProfile).toHaveBeenCalledWith('Ana', 'Silva')
    expect(await screen.findByText('Ana S. #2')).toBeVisible()
    expect(onCompleted).not.toHaveBeenCalled()

    await user.click(screen.getByRole('button', { name: /continuar/i }))
    expect(onCompleted).toHaveBeenCalledOnce()
  })
})
