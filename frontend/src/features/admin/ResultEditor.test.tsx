import '@testing-library/jest-dom/vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import type { ManualResultDraft } from '@/api/client'

import { ResultEditor } from './ResultEditor'

const emptyDraft: ManualResultDraft = {
  goals: [],
  homeYellowCards: 0,
  awayYellowCards: 0,
  homeRedCards: 0,
  awayRedCards: 0,
  penaltyWinnerTeamFifaCode: null,
}

describe('ResultEditor', () => {
  it('adds a goal and scopes the player selector to its selected team', async () => {
    const user = userEvent.setup()
    const onSave = vi.fn().mockResolvedValue(undefined)
    render(<ResultEditor value={emptyDraft} homeTeamFifaCode="BRA" awayTeamFifaCode="ARG" saving={false} onSave={onSave} />)

    await user.click(screen.getByRole('button', { name: 'Adicionar gol' }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Time do gol 1' }), 'BRA')
    await user.type(screen.getByRole('combobox', { name: 'Jogador do gol 1' }), 'Alisson')

    expect(screen.getByRole('option', { name: 'Alisson' })).toBeVisible()
    expect(screen.queryByRole('option', { name: 'Juan Musso' })).not.toBeInTheDocument()
    await user.keyboard('{ArrowDown}{Enter}')
    await user.click(screen.getByRole('button', { name: 'Salvar resultado' }))
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({
      goals: [{ teamFifaCode: 'BRA', playerKey: 'BRA:1' }],
    }))
  })

  it('reorders and removes goals while preserving their selections', async () => {
    const user = userEvent.setup()
    const value = { ...emptyDraft, goals: [
      { teamFifaCode: 'BRA', playerKey: 'BRA:1' },
      { teamFifaCode: 'ARG', playerKey: 'ARG:1' },
    ] }
    render(<ResultEditor value={value} homeTeamFifaCode="BRA" awayTeamFifaCode="ARG" saving={false} onSave={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Mover gol 1 para cima' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Mover gol 2 para baixo' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Mover gol 2 para cima' }))
    expect(screen.getAllByRole('combobox', { name: /jogador do gol/i })[0]).toHaveValue('Juan Musso')
    await user.click(screen.getByRole('button', { name: 'Remover gol 1' }))
    expect(screen.getAllByRole('combobox', { name: /jogador do gol/i })).toHaveLength(1)
    expect(screen.getByText('Placar: 1 × 0')).toBeVisible()
  })

  it('enables penalties only for a draw and clears the winner when score stops being tied', async () => {
    const user = userEvent.setup()
    const value = { ...emptyDraft, goals: [
      { teamFifaCode: 'BRA', playerKey: 'BRA:1' },
      { teamFifaCode: 'ARG', playerKey: 'ARG:1' },
    ] }
    render(<ResultEditor value={value} homeTeamFifaCode="BRA" awayTeamFifaCode="ARG" saving={false} onSave={vi.fn()} />)

    const winner = screen.getByRole('combobox', { name: 'Ganhador nos pênaltis' })
    expect(winner).toBeEnabled()
    expect(screen.getByText('Para escohler ganhador nos penaltis, o placar tem que ser um empate')).toBeVisible()
    expect(Array.from(winner.querySelectorAll('option')).map((option) => option.value)).toEqual(['', 'BRA', 'ARG'])
    await user.selectOptions(winner, 'ARG')
    await user.click(screen.getByRole('button', { name: 'Remover gol 2' }))
    expect(winner).toBeDisabled()
    expect(winner).toHaveValue('')
  })

  it('keeps goal row identity stable when reordering', async () => {
    const user = userEvent.setup()
    const value = { ...emptyDraft, goals: [
      { teamFifaCode: 'BRA', playerKey: 'BRA:1' },
      { teamFifaCode: 'ARG', playerKey: 'ARG:1' },
    ] }
    render(<ResultEditor value={value} homeTeamFifaCode="BRA" awayTeamFifaCode="ARG" saving={false} onSave={vi.fn()} />)

    const argentinaPlayerInput = screen.getAllByRole('combobox', { name: /jogador do gol/i })[1]
    await user.click(screen.getByRole('button', { name: 'Mover gol 2 para cima' }))

    expect(screen.getAllByRole('combobox', { name: /jogador do gol/i })[0]).toBe(argentinaPlayerInput)
  })

  it('validates incomplete goals and invalid card totals before saving', async () => {
    const user = userEvent.setup()
    const onSave = vi.fn()
    render(<ResultEditor value={emptyDraft} homeTeamFifaCode="BRA" awayTeamFifaCode="ARG" saving={false} onSave={onSave} />)

    await user.click(screen.getByRole('button', { name: 'Adicionar gol' }))
    fireEvent.change(screen.getByLabelText('Amarelos mandante'), { target: { value: '-1' } })
    await user.click(screen.getByRole('button', { name: 'Salvar resultado' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/preencha o time e o jogador de todos os gols/i)
    expect(screen.getByRole('alert')).toHaveTextContent(/cartões devem ser números inteiros não negativos/i)
    expect(onSave).not.toHaveBeenCalled()
  })
})
