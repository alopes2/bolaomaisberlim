import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { PlayerCombobox } from './PlayerCombobox'

const players = [
  { key: 'BRA:7', name: 'Vinícius Júnior' },
  { key: 'BRA:10', name: 'Rodrygo' },
]

describe('PlayerCombobox', () => {
  it('filters without accents and only returns roster options', async () => {
    const user = userEvent.setup()
    render(
      <PlayerCombobox
        label="Primeiro gol"
        players={players}
        value={null}
        onChange={vi.fn()}
      />,
    )

    await user.type(
      screen.getByRole('combobox', { name: /primeiro gol/i }),
      'vinicius',
    )

    expect(screen.getByRole('option', { name: /vinícius/i })).toBeVisible()
    expect(screen.queryByText(/cadastrar vinicius/i)).not.toBeInTheDocument()
  })

  it('selects a roster player with the keyboard', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(
      <PlayerCombobox
        label="Artilheiro Brasil"
        players={players}
        value={null}
        onChange={onChange}
      />,
    )

    const combobox = screen.getByRole('combobox', {
      name: /artilheiro brasil/i,
    })
    await user.type(combobox, 'rodrygo')
    await user.keyboard('{ArrowDown}{Enter}')

    expect(onChange).toHaveBeenCalledWith('BRA:10')
  })
})
