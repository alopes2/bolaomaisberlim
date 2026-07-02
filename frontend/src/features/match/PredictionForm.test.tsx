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
        homeTeamFifaCode="BRA"
        awayTeam="Alemanha"
        awayTeamFifaCode="GER"
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
        homeTeamFifaCode="BRA"
        awayTeam="Alemanha"
        awayTeamFifaCode="GER"
        homePlayers={homePlayers}
        awayPlayers={awayPlayers}
        cutoffAt="2026-06-29T16:50:00Z"
        onSubmit={vi.fn()}
      />,
    )

    expect(screen.getByRole('button', { name: /palpites encerrados/i })).toBeDisabled()
  })

  it('only enables the penalty winner for a draw and clears it after the score changes', async () => {
    const user = userEvent.setup()
    render(
      <PredictionForm
        homeTeam="Brasil"
        homeTeamFifaCode="BRA"
        awayTeam="Alemanha"
        awayTeamFifaCode="GER"
        homePlayers={homePlayers}
        awayPlayers={awayPlayers}
        cutoffAt="2026-06-29T16:50:00Z"
        onSubmit={vi.fn()}
      />,
    )

    const winner = screen.getByRole('combobox', { name: /ganhador nos pênaltis/i })
    expect(winner).toBeEnabled()
    expect(screen.getByRole('option', { name: 'Brasil' })).toBeVisible()
    expect(screen.getByRole('option', { name: 'Alemanha' })).toBeVisible()

    await user.selectOptions(winner, 'BRA')
    expect(winner).toHaveValue('BRA')
    await user.clear(screen.getByLabelText('Brasil'))
    await user.type(screen.getByLabelText('Brasil'), '1')

    expect(winner).toBeDisabled()
    expect(winner).toHaveValue('')
    expect(screen.getByText('Para escohler ganhador nos penaltis, o placar tem que ser um empate')).toBeVisible()
  })

  it('restores and submits the stored penalty winner', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(
      <PredictionForm
        homeTeam="Brasil"
        homeTeamFifaCode="BRA"
        awayTeam="Alemanha"
        awayTeamFifaCode="GER"
        homePlayers={homePlayers}
        awayPlayers={awayPlayers}
        cutoffAt="2026-06-29T16:50:00Z"
        storedPrediction={{
          homeGoals: 1, awayGoals: 1, firstScorerKey: 'BRA:7',
          homeTopScorerKey: 'BRA:7', awayTopScorerKey: 'GER:10',
          homeYellowCards: 0, awayYellowCards: 0, homeRedCards: 0,
          awayRedCards: 0, penaltyWinnerTeamFifaCode: 'GER',
        }}
        onSubmit={onSubmit}
      />,
    )

    expect(screen.getByRole('combobox', { name: /ganhador nos pênaltis/i })).toHaveValue('GER')
    await user.click(screen.getByRole('button', { name: /salvar palpite/i }))
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ penaltyWinnerTeamFifaCode: 'GER' }),
      expect.anything(),
    )
  })
})
