import '@testing-library/jest-dom/vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { AdminMatchPage } from './AdminMatchPage'

describe('AdminMatchPage', () => {
  it('shows provider data and disables confirmation while mappings are unresolved', async () => {
    const api = {
      getAdminResult: vi.fn().mockResolvedValue({
        providerStatus: 'FT',
        result: {
          homeGoals: 2,
          awayGoals: 1,
          firstScorerKey: null,
          homeTopScorerKeys: ['BRA:10'],
          awayTopScorerKeys: ['ARG:9'],
          homeYellowCards: 2,
          awayYellowCards: 3,
          homeRedCards: 0,
          awayRedCards: 1,
        },
        unresolvedPlayers: [
          { providerPlayerId: 10, providerName: 'Jogador API', teamFifaCode: 'BRA' },
        ],
        homeGoalEvents: 2,
        awayGoalEvents: 1,
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

    expect(await screen.findByText('FT')).toBeVisible()
    expect(screen.getByText('Jogador API')).toBeVisible()
    expect(screen.getByText('Ana S.')).toBeVisible()
    expect(screen.getByRole('button', { name: /confirmar resultado/i })).toBeDisabled()
  })
})
