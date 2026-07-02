import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import '@testing-library/jest-dom/vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import type { AdminApi, AdminMatchesResponse, AdminTeam } from '@/api/client'

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

const teams: AdminTeam[] = [
  { fifaCode: 'BRA', name: 'Brasil', flagIcon: '🇧🇷', eliminated: false },
  { fifaCode: 'ARG', name: 'Argentina', flagIcon: '🇦🇷', eliminated: false },
  { fifaCode: 'FRA', name: 'França', flagIcon: '🇫🇷', eliminated: true },
  { fifaCode: 'GER', name: 'Alemanha', flagIcon: '🇩🇪', eliminated: true },
]

function api(overrides: Partial<AdminApi> = {}): AdminApi {
  return {
    getAdminMatches: vi.fn().mockResolvedValue(matches),
    getAdminTeams: vi.fn().mockResolvedValue(teams),
    setTeamEliminated: vi.fn(),
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

  it('creates a match from available teams without asking for an ID', async () => {
    const user = userEvent.setup()
    const createAdminMatch = vi.fn().mockResolvedValue(undefined)
    renderPage(api({ createAdminMatch }))

    await screen.findByText('Adicionar jogo manualmente')
    expect(screen.queryByLabelText('ID do jogo')).not.toBeInTheDocument()
    await user.type(screen.getByLabelText('Data e hora em Europe/Berlin'), '2026-06-15T18:00')
    expect(within(screen.getByLabelText('Mandante')).getByRole('option', { name: '🇧🇷 Brasil (BRA)' })).toBeInTheDocument()
    expect(within(screen.getByLabelText('Mandante')).queryByRole('option', { name: '🇫🇷 França (FRA)' })).not.toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Mandante'), 'BRA')
    expect(within(screen.getByLabelText('Visitante')).queryByRole('option', { name: '🇧🇷 Brasil (BRA)' })).not.toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Visitante'), 'ARG')
    await user.click(screen.getByRole('button', { name: 'Adicionar jogo' }))

    await waitFor(() => expect(createAdminMatch).toHaveBeenCalledWith({
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
    expect(within(screen.getByLabelText('Mandante do jogo')).getByRole('option', { name: '🇧🇷 Brasil (BRA)' })).toBeInTheDocument()
    expect(within(screen.getByLabelText('Visitante do jogo')).queryByRole('option', { name: '🇫🇷 França (FRA)' })).not.toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Visitante do jogo'), '')
    await user.selectOptions(screen.getByLabelText('Mandante do jogo'), 'ARG')
    await user.selectOptions(screen.getByLabelText('Visitante do jogo'), 'BRA')
    await user.click(screen.getByRole('button', { name: 'Salvar alterações' }))

    await waitFor(() => expect(updateAdminMatch).toHaveBeenCalledWith('active', {
      kickoff: '2026-07-05T16:30:00.000Z',
      homeTeamFifaCode: 'ARG',
      awayTeamFifaCode: 'BRA',
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
    await user.selectOptions(screen.getByLabelText('Mandante do jogo'), 'ARG')
    await user.click(screen.getByRole('button', { name: 'Salvar alterações' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Jogo não encontrado.')
    expect(screen.getByLabelText('Mandante do jogo')).toHaveValue('ARG')
  })

  it('keeps only the current eliminated team available on its edit side', async () => {
    const user = userEvent.setup()
    renderPage(api())

    await user.click((await screen.findAllByRole('button', { name: 'Editar jogo' }))[0])

    const home = screen.getByLabelText('Mandante do jogo')
    const away = screen.getByLabelText('Visitante do jogo')
    expect(within(home).getByRole('option', { name: '🇩🇪 Alemanha (GER)' })).toBeInTheDocument()
    expect(within(home).queryByRole('option', { name: '🇫🇷 França (FRA)' })).not.toBeInTheDocument()
    expect(within(away).getByRole('option', { name: '🇫🇷 França (FRA)' })).toBeInTheDocument()
    expect(within(away).queryByRole('option', { name: '🇩🇪 Alemanha (GER)' })).not.toBeInTheDocument()
  })

  it('updates one team elimination status and refreshes the catalog', async () => {
    const user = userEvent.setup()
    let resolveUpdate!: () => void
    const setTeamEliminated = vi.fn().mockImplementation(() => new Promise<void>(resolve => { resolveUpdate = resolve }))
    const { invalidateQueries } = renderPage(api({ setTeamEliminated }))

    const management = (await screen.findByText('Gerenciar seleções')).closest<HTMLElement>('[data-slot="card"]')!
    expect(within(management).getAllByText('Eliminada')).toHaveLength(2)
    const eliminateBrasil = within(management).getByRole('button', { name: 'Marcar Brasil como eliminada' })
    const restoreFrance = within(management).getByRole('button', { name: 'Restaurar França' })
    await user.click(eliminateBrasil)

    expect(setTeamEliminated).toHaveBeenCalledWith('BRA', true)
    expect(eliminateBrasil).toBeDisabled()
    expect(restoreFrance).toBeEnabled()
    resolveUpdate()
    await waitFor(() => expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['admin-teams'] }))
  })

  it('tracks overlapping team updates independently', async () => {
    const user = userEvent.setup()
    const resolvers = new Map<string, () => void>()
    const setTeamEliminated = vi.fn().mockImplementation((fifaCode: string) =>
      new Promise<void>(resolve => { resolvers.set(fifaCode, resolve) }))
    renderPage(api({ setTeamEliminated }))

    const brasil = await screen.findByRole('button', { name: 'Marcar Brasil como eliminada' })
    const argentina = screen.getByRole('button', { name: 'Marcar Argentina como eliminada' })
    const france = screen.getByRole('button', { name: 'Restaurar França' })

    await user.click(brasil)
    await user.click(argentina)
    expect(brasil).toBeDisabled()
    expect(argentina).toBeDisabled()
    expect(france).toBeEnabled()

    resolvers.get('ARG')!()
    await waitFor(() => expect(argentina).toBeEnabled())
    expect(brasil).toBeDisabled()

    resolvers.get('BRA')!()
    await waitFor(() => expect(brasil).toBeEnabled())
  })

  it('shows an accessible team update error', async () => {
    const user = userEvent.setup()
    renderPage(api({
      setTeamEliminated: vi.fn().mockRejectedValue(new Error('Não foi possível atualizar a seleção.')),
    }))

    await user.click(await screen.findByRole('button', { name: 'Restaurar França' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível atualizar a seleção.')
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
