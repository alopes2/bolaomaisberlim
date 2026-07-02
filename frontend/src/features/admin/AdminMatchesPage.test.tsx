import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import '@testing-library/jest-dom/vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import type { AdminApi, AdminMatchesResponse } from '@/api/client'

import { AdminMatchesPage } from './AdminMatchesPage'

const matches: AdminMatchesResponse = {
  matches: [
    {
      id: 'later',
      kickoff: '2026-07-03T18:00:00Z',
      homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'FRA',
      status: 'Upcoming',
      resultConfirmed: false,
    },
    {
      id: 'active',
      kickoff: '2026-07-02T18:00:00Z',
      homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'ARG',
      status: 'Active',
      resultConfirmed: true,
    },
    {
      id: 'old',
      kickoff: '2026-07-01T18:00:00Z',
      homeTeamFifaCode: 'GER',
      awayTeamFifaCode: 'FRA',
      status: 'Closed',
      resultConfirmed: true,
    },
    {
      id: 'other',
      kickoff: '2026-07-04T18:00:00Z',
      homeTeamFifaCode: 'GER',
      awayTeamFifaCode: 'ARG',
      status: 'Archived',
      resultConfirmed: true,
    },
  ],
}

function api(overrides: Partial<AdminApi> = {}): AdminApi {
  return {
    getAdminMatches: vi.fn().mockResolvedValue(matches),
    createAdminMatch: vi.fn(),
    updateAdminMatch: vi.fn(),
    getAdminResult: vi.fn(),
    getProvisionalLeaderboard: vi.fn(),
    saveAdminResult: vi.fn(),
    confirmResult: vi.fn(),
    finishMatch: vi.fn(),
    ...overrides,
  }
}

function renderPage(adminApi: AdminApi) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries')
  render(
    <QueryClientProvider client={queryClient}>
      <AdminMatchesPage api={adminApi} />
    </QueryClientProvider>,
  )
  return { invalidateQueries }
}

async function finishActiveMatch() {
  const user = userEvent.setup()
  await user.click(await screen.findByRole('button', { name: 'Finalizar jogo atual' }))
  await user.click(screen.getByRole('button', { name: 'Finalizar jogo' }))
}

describe('AdminMatchesPage', () => {
  it('shows ordered matches and lifecycle statuses without provider controls', async () => {
    renderPage(api())

    const links = await screen.findAllByRole('link', { name: 'Apurar resultado' })
    expect(links.map(link => link.getAttribute('href'))).toEqual([
      '/admin?matchId=old',
      '/admin?matchId=active',
      '/admin?matchId=later',
      '/admin?matchId=other',
    ])
    for (const status of ['Encerrado', 'Ativo', 'Próximo', 'Arquivado']) {
      expect(screen.getByText(status)).toBeInTheDocument()
    }
    expect(screen.queryByText(/API-Football/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /sincronizar/i })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('ID do fixture')).not.toBeInTheDocument()
  })

  it('creates a match without provider data', async () => {
    const user = userEvent.setup()
    const createAdminMatch = vi.fn().mockResolvedValue(undefined)
    renderPage(api({ createAdminMatch }))

    await screen.findByText('Adicionar jogo manualmente')
    await user.type(screen.getByLabelText('ID do jogo'), 'manual-1')
    await user.type(screen.getByLabelText('Data e hora em Europe/Berlin'), '2026-06-15T18:00')
    await user.type(screen.getByLabelText('Mandante'), 'bra')
    await user.type(screen.getByLabelText('Visitante'), 'arg')
    await user.click(screen.getByRole('button', { name: 'Adicionar jogo' }))

    await waitFor(() => expect(createAdminMatch).toHaveBeenCalledWith({
      id: 'manual-1',
      homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'ARG',
      kickoff: '2026-06-15T16:00:00.000Z',
      prizeHandedOverAt: null,
    }))
    expect(await screen.findByText('Jogo adicionado.')).toBeInTheDocument()
  })

  it('edits kickoff and teams while keeping the match ID immutable', async () => {
    const user = userEvent.setup()
    const updateAdminMatch = vi.fn().mockResolvedValue(undefined)
    const { invalidateQueries } = renderPage(api({ updateAdminMatch }))

    await user.click((await screen.findAllByRole('button', { name: 'Editar jogo' }))[1])

    const immutableId = screen.getByText('ID do jogo: active')
    expect(immutableId).toBeInTheDocument()
    expect(within(immutableId.closest('[data-slot="card"]')!).queryByRole('textbox', { name: 'ID do jogo' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Data e hora do jogo em Europe/Berlin')).toHaveValue('2026-07-02T20:00')

    await user.clear(screen.getByLabelText('Data e hora do jogo em Europe/Berlin'))
    await user.type(screen.getByLabelText('Data e hora do jogo em Europe/Berlin'), '2026-07-05T18:30')
    await user.clear(screen.getByLabelText('Mandante do jogo'))
    await user.type(screen.getByLabelText('Mandante do jogo'), 'ger')
    await user.clear(screen.getByLabelText('Visitante do jogo'))
    await user.type(screen.getByLabelText('Visitante do jogo'), 'fra')
    await user.click(screen.getByRole('button', { name: 'Salvar alterações' }))

    await waitFor(() => expect(updateAdminMatch).toHaveBeenCalledWith('active', {
      kickoff: '2026-07-05T16:30:00.000Z',
      homeTeamFifaCode: 'GER',
      awayTeamFifaCode: 'FRA',
      prizeHandedOverAt: null,
    }))
    expect(await screen.findByText('Jogo atualizado.')).toBeInTheDocument()
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['admin-matches'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['current-match'] })
  })

  it('cancels editing without saving', async () => {
    const user = userEvent.setup()
    const updateAdminMatch = vi.fn()
    renderPage(api({ updateAdminMatch }))

    await user.click((await screen.findAllByRole('button', { name: 'Editar jogo' }))[0])
    await user.click(screen.getByRole('button', { name: 'Cancelar edição' }))

    expect(screen.queryByText('Editar jogo cadastrado')).not.toBeInTheDocument()
    expect(updateAdminMatch).not.toHaveBeenCalled()
  })

  it('keeps edit values and shows API errors', async () => {
    const user = userEvent.setup()
    renderPage(api({
      updateAdminMatch: vi.fn().mockRejectedValue(new Error('Jogo não encontrado.')),
    }))

    await user.click((await screen.findAllByRole('button', { name: 'Editar jogo' }))[0])
    await user.clear(screen.getByLabelText('Mandante do jogo'))
    await user.type(screen.getByLabelText('Mandante do jogo'), 'arg')
    await user.click(screen.getByRole('button', { name: 'Salvar alterações' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Jogo não encontrado.')
    expect(screen.getByLabelText('Mandante do jogo')).toHaveValue('arg')
  })

  it('shows the finish action only for the active match', async () => {
    renderPage(api())

    expect(await screen.findAllByRole('button', { name: 'Finalizar jogo atual' })).toHaveLength(1)
  })

  it('disables finishing until the active result is confirmed', async () => {
    renderPage(api({
      getAdminMatches: vi.fn().mockResolvedValue({
        matches: matches.matches.map(match => match.id === 'active'
          ? { ...match, resultConfirmed: false }
          : match),
      }),
    }))

    expect(await screen.findByRole('button', { name: 'Finalizar jogo atual' })).toBeDisabled()
    expect(screen.getByText('Confirme o resultado antes de finalizar o jogo.')).toBeInTheDocument()
  })

  it('reports the next activated match and refreshes lifecycle queries', async () => {
    const finishMatch = vi.fn().mockResolvedValue({ closedMatchId: 'active', activatedMatchId: 'later' })
    const { invalidateQueries } = renderPage(api({ finishMatch }))

    await finishActiveMatch()

    expect(finishMatch).toHaveBeenCalledWith('active')
    expect(await screen.findByText('Jogo finalizado. Próximo jogo ativado: later.')).toBeInTheDocument()
    for (const queryKey of ['admin-matches', 'current-match', 'match-history', 'leaderboard']) {
      expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: [queryKey] })
    }
  })

  it('asks the admin to add a match when none was activated', async () => {
    renderPage(api({
      finishMatch: vi.fn().mockResolvedValue({ closedMatchId: 'active', activatedMatchId: null }),
    }))

    await finishActiveMatch()

    expect(await screen.findByText('Jogo finalizado. Adicione o próximo jogo.')).toBeInTheDocument()
  })

  it('shows a finish failure returned by the API', async () => {
    renderPage(api({
      finishMatch: vi.fn().mockRejectedValue(new Error('O jogo selecionado não está ativo.')),
    }))

    await finishActiveMatch()

    expect(await screen.findByRole('alert')).toHaveTextContent('O jogo selecionado não está ativo.')
  })
})
