import '@testing-library/jest-dom/vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { AdminMatchPage } from './AdminMatchPage'

describe('AdminMatchPage', () => {
  it('loads the selected match teams, manual draft, and provisional leaderboard', async () => {
    const api = {
      getAdminMatches: vi.fn().mockResolvedValue({ matches: [{
        id: 'match-1', kickoff: '2026-07-02T18:00:00Z', homeTeamFifaCode: 'BRA',
        awayTeamFifaCode: 'ARG', status: 'Active', resultConfirmed: false,
      }] }),
      getAdminResult: vi.fn().mockResolvedValue({
        goals: [], homeYellowCards: 0, awayYellowCards: 0,
        homeRedCards: 0, awayRedCards: 0, penaltyWinnerTeamFifaCode: null,
      }),
      getProvisionalLeaderboard: vi.fn().mockResolvedValue({
        entries: [{ position: 1, publicName: 'Ana S.', totalPoints: 12, exactScoreCount: 1, firstScorerCount: 0 }],
        roundWinner: { publicName: 'Ana S.', points: 12 },
      }),
      saveAdminResult: vi.fn(),
      confirmResult: vi.fn(),
    }
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    render(
      <QueryClientProvider client={queryClient}>
        <AdminMatchPage api={api} matchId="match-1" />
      </QueryClientProvider>,
    )

    expect(await screen.findByText(/BRA × ARG/)).toBeVisible()
    expect(screen.getByText('Ana S.')).toBeVisible()
    expect(screen.getByRole('button', { name: /confirmar resultado/i })).toBeEnabled()
    expect(screen.queryByText(/status do provedor/i)).not.toBeInTheDocument()
    expect(api.getAdminMatches).toHaveBeenCalledOnce()
  })

  it('requires saving edits before confirmation and stays gated while saving', async () => {
    const user = userEvent.setup()
    let resolveSave!: () => void
    const savePromise = new Promise<void>((resolve) => { resolveSave = resolve })
    const api = createApi({ saveAdminResult: vi.fn().mockReturnValue(savePromise) })
    renderPage(api)

    const confirm = await screen.findByRole('button', { name: /confirmar resultado/i })
    await user.clear(screen.getByLabelText('Amarelos mandante'))
    await user.type(screen.getByLabelText('Amarelos mandante'), '2')
    expect(confirm).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Salvar resultado' }))
    expect(screen.getByRole('button', { name: 'Salvar resultado' })).toBeDisabled()
    expect(confirm).toBeDisabled()
    await act(async () => resolveSave())
    expect(confirm).toBeEnabled()
  })

  it('shows save and confirmation errors accessibly', async () => {
    const user = userEvent.setup()
    const api = createApi({
      saveAdminResult: vi.fn().mockRejectedValue(new Error('save failed')),
      confirmResult: vi.fn().mockRejectedValue(new Error('confirm failed')),
    })
    renderPage(api)

    await screen.findByText(/BRA × ARG/)
    await user.click(screen.getByRole('button', { name: 'Salvar resultado' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/não foi possível salvar o resultado/i)
    await user.click(screen.getByRole('button', { name: /confirmar resultado/i }))
    await user.click(await screen.findByRole('button', { name: /^confirmar$/i }))
    expect(await screen.findByText(/não foi possível confirmar o resultado/i)).toHaveAttribute('role', 'alert')
  })
})

function createApi(overrides = {}) {
  return {
    getAdminMatches: vi.fn().mockResolvedValue({ matches: [{
      id: 'match-1', kickoff: '2026-07-02T18:00:00Z', homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'ARG', status: 'Active', resultConfirmed: false,
    }] }),
    getAdminResult: vi.fn().mockResolvedValue({
      goals: [], homeYellowCards: 0, awayYellowCards: 0,
      homeRedCards: 0, awayRedCards: 0, penaltyWinnerTeamFifaCode: null,
    }),
    getProvisionalLeaderboard: vi.fn().mockResolvedValue({ entries: [], roundWinner: null }),
    saveAdminResult: vi.fn().mockResolvedValue(undefined),
    confirmResult: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  }
}

function renderPage(api: ReturnType<typeof createApi>) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <AdminMatchPage api={api} matchId="match-1" />
    </QueryClientProvider>,
  )
}
