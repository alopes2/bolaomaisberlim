import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { PredictionForm } from './PredictionForm'

const homePlayers = [{ key: 'BRA:7', name: 'Vinícius Júnior' }]
const awayPlayers = [{ key: 'GER:10', name: 'Jamal Musiala' }]

describe('PredictionForm', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.setSystemTime(new Date('2026-06-29T15:00:00Z'))
  })

  afterEach(() => vi.useRealTimers())

  it('restricts each top-scorer field to its team roster', async () => {
    const user = userEvent.setup()
    render(
      <PredictionForm
        homeTeam="Brasil"
        awayTeam="Alemanha"
        homePlayers={homePlayers}
        awayPlayers={awayPlayers}
        cutoffAt="2026-06-29T16:50:00Z"
        onSubmit={vi.fn()}
      />,
    )

    await user.click(
      screen.getByRole('combobox', { name: /artilheiro brasil/i }),
    )

    expect(screen.getByRole('option', { name: /vinícius/i })).toBeVisible()
    expect(screen.queryByRole('option', { name: /musiala/i })).toBeNull()
  })

  it('disables submission at the cutoff', () => {
    vi.setSystemTime(new Date('2026-06-29T16:50:00Z'))

    render(
      <PredictionForm
        homeTeam="Brasil"
        awayTeam="Alemanha"
        homePlayers={homePlayers}
        awayPlayers={awayPlayers}
        cutoffAt="2026-06-29T16:50:00Z"
        onSubmit={vi.fn()}
      />,
    )

    expect(screen.getByRole('button', { name: /palpites encerrados/i })).toBeDisabled()
  })
})
