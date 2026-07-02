import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import '@testing-library/jest-dom/vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import type { AdminApi, AdminMatchesResponse } from '@/api/client'

import { AdminMatchesPage } from './AdminMatchesPage'

const matches: AdminMatchesResponse = {
  providerCallAvailable: true,
  lastSuccessfulSyncAt: null,
  matches: [
    {
      id: 'later',
      providerFixtureId: 3,
      kickoff: '2026-07-03T18:00:00Z',
      homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'FRA',
      providerStatus: 'NS',
      status: 'Upcoming',
    },
    {
      id: 'active',
      providerFixtureId: 2,
      kickoff: '2026-07-02T18:00:00Z',
      homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'ARG',
      providerStatus: 'NS',
      status: 'Active',
    },
    {
      id: 'old',
      providerFixtureId: 1,
      kickoff: '2026-07-01T18:00:00Z',
      homeTeamFifaCode: 'GER',
      awayTeamFifaCode: 'FRA',
      providerStatus: 'FT',
      status: 'Closed',
    },
    {
      id: 'other',
      providerFixtureId: 4,
      kickoff: '2026-07-04T18:00:00Z',
      homeTeamFifaCode: 'GER',
      awayTeamFifaCode: 'ARG',
      providerStatus: 'NS',
      status: 'Archived',
    },
  ],
}

function api(overrides: Partial<AdminApi> = {}): AdminApi {
  return {
    getAdminMatches: vi.fn().mockResolvedValue(matches),
    syncWorldCupMatches: vi.fn(),
    createAdminMatch: vi.fn(),
    getAdminResult: vi.fn(),
    getProvisionalLeaderboard: vi.fn(),
    saveAdminResult: vi.fn(),
    confirmResult: vi.fn(),
    ...overrides,
  }
}

function renderPage(adminApi: AdminApi) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(
    <QueryClientProvider client={queryClient}>
      <AdminMatchesPage api={adminApi} />
    </QueryClientProvider>,
  )
}

describe('AdminMatchesPage', () => {
  it('shows ordered matches, all statuses, and result links', async () => {
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
    expect(screen.getByText(/consultará o API-Football/i)).toBeInTheDocument()
  })

  it('reports provider synchronization counts and skipped fixtures', async () => {
    const user = userEvent.setup()
    const syncWorldCupMatches = vi.fn().mockResolvedValue({
      providerFetchPerformed: true,
      lastSuccessfulSyncAt: '2026-06-30T08:00:00Z',
      createdCount: 3,
      updatedCount: 2,
      statusChangeCount: 1,
      skippedFixtures: [
        { fixtureId: 99, reasonCode: 'unsupported_team_code' },
        { fixtureId: 100, reasonCode: 'missing_fifa_code' },
      ],
    })
    renderPage(api({
      getAdminMatches: vi.fn()
        .mockResolvedValueOnce(matches)
        .mockResolvedValueOnce({ ...matches, providerCallAvailable: false }),
      syncWorldCupMatches,
    }))

    await user.click(await screen.findByRole('button', { name: 'Sincronizar jogos' }))

    expect(await screen.findByText(/3 criados, 2 atualizados e 1 status alterado/i)).toBeInTheDocument()
    expect(screen.getByText(/Fixture 99: um dos códigos FIFA não é suportado/i)).toBeInTheDocument()
    expect(screen.getByText(/Fixture 100: está faltando um código FIFA/i)).toBeInTheDocument()
    expect(screen.getByText(/A próxima sincronização hoje apenas recalculará os status/i)).toBeInTheDocument()
  })

  it('explains a same-day local recalculation and keeps sync available', async () => {
    const user = userEvent.setup()
    const syncWorldCupMatches = vi.fn().mockResolvedValue({
      providerFetchPerformed: false,
      lastSuccessfulSyncAt: '2026-06-30T08:00:00Z',
      createdCount: 0,
      updatedCount: 0,
      statusChangeCount: 2,
      skippedFixtures: [],
    })
    renderPage(api({
      getAdminMatches: vi.fn().mockResolvedValue({
        ...matches,
        providerCallAvailable: false,
        lastSuccessfulSyncAt: '2026-06-30T08:00:00Z',
      }),
      syncWorldCupMatches,
    }))

    expect(await screen.findByText(/apenas recalculará os status/i)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Sincronizar jogos' }))

    expect(await screen.findByText(/Nenhuma consulta ao API-Football foi feita.*2 status alterados/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sincronizar jogos' })).toBeEnabled()
  })

  it('uses the authoritative refetched availability after synchronization', async () => {
    const user = userEvent.setup()
    const getAdminMatches = vi.fn()
      .mockResolvedValueOnce({ ...matches, providerCallAvailable: false })
      .mockResolvedValueOnce({ ...matches, providerCallAvailable: true })
    renderPage(api({
      getAdminMatches,
      syncWorldCupMatches: vi.fn().mockResolvedValue({
        providerFetchPerformed: false,
        lastSuccessfulSyncAt: '2026-06-30T08:00:00Z',
        createdCount: 0,
        updatedCount: 0,
        statusChangeCount: 0,
        skippedFixtures: [],
      }),
    }))

    expect(await screen.findByText(/apenas recalculará os status/i)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Sincronizar jogos' }))

    expect(await screen.findByText(/consultará o API-Football/i)).toBeInTheDocument()
    expect(getAdminMatches).toHaveBeenCalledTimes(2)
  })

  it('validates the manual form before submitting', async () => {
    const user = userEvent.setup()
    const createAdminMatch = vi.fn()
    renderPage(api({ createAdminMatch }))

    await user.click(await screen.findByRole('button', { name: 'Adicionar jogo' }))

    const error = screen.getByRole('alert')
    expect(error).toHaveTextContent('Preencha todos os campos obrigatórios.')
    expect(screen.getByLabelText('ID do jogo')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText('ID do jogo')).toHaveAttribute('aria-describedby', error.id)
    expect(createAdminMatch).not.toHaveBeenCalled()
  })

  it.each(['0', '-1', '1.5'])('rejects invalid fixture ID %s', async fixtureId => {
    const user = userEvent.setup()
    const createAdminMatch = vi.fn()
    renderPage(api({ createAdminMatch }))

    await screen.findByText('Adicionar jogo manualmente')
    await user.type(screen.getByLabelText('ID do jogo'), 'manual-1')
    await user.type(screen.getByLabelText('ID do fixture'), fixtureId)
    await user.type(screen.getByLabelText('Data e hora em Europe/Berlin'), '2026-07-01T18:00')
    await user.type(screen.getByLabelText('Mandante'), 'BRA')
    await user.type(screen.getByLabelText('Visitante'), 'ARG')
    await user.click(screen.getByRole('button', { name: 'Adicionar jogo' }))

    const error = screen.getByRole('alert')
    expect(error).toHaveTextContent('Informe um ID de fixture inteiro e positivo.')
    expect(screen.getByLabelText('ID do fixture')).toHaveAttribute('aria-invalid', 'true')
    expect(createAdminMatch).not.toHaveBeenCalled()
  })

  it('retains manual values on failure and clears them after success', async () => {
    const user = userEvent.setup()
    const createAdminMatch = vi
      .fn()
      .mockRejectedValueOnce(new Error("Match 'manual-1' already exists."))
      .mockResolvedValueOnce(undefined)
    renderPage(api({ createAdminMatch }))

    await screen.findByText('Adicionar jogo manualmente')
    await user.type(screen.getByLabelText('ID do jogo'), 'manual-1')
    await user.type(screen.getByLabelText('ID do fixture'), '123')
    await user.type(screen.getByLabelText('Data e hora em Europe/Berlin'), '2026-06-15T18:00')
    await user.type(screen.getByLabelText('Mandante'), 'bra')
    await user.type(screen.getByLabelText('Visitante'), 'arg')
    await user.click(screen.getByRole('button', { name: 'Adicionar jogo' }))

    expect(await screen.findByText("Match 'manual-1' already exists.")).toBeInTheDocument()
    expect(screen.getByLabelText('ID do jogo')).toHaveValue('manual-1')

    await user.click(screen.getByRole('button', { name: 'Adicionar jogo' }))

    expect(await screen.findByText('Jogo adicionado.')).toBeInTheDocument()
    expect(screen.getByLabelText('ID do jogo')).toHaveValue('')
    expect(createAdminMatch).toHaveBeenLastCalledWith(expect.objectContaining({
      id: 'manual-1',
      providerFixtureId: 123,
      homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'ARG',
      kickoff: '2026-06-15T16:00:00.000Z',
    }))
    expect(screen.getByRole('status')).toHaveTextContent('Jogo adicionado.')
  })

  it('shows phase-aware sync errors', async () => {
    const user = userEvent.setup()
    renderPage(api({
      syncWorldCupMatches: vi.fn().mockRejectedValue(new Error(
        'Os jogos foram importados, mas os status não foram atualizados. Tente sincronizar novamente.',
      )),
    }))

    await user.click(await screen.findByRole('button', { name: 'Sincronizar jogos' }))

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/Os jogos foram importados/i))
  })
})
