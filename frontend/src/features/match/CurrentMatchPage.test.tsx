import '@testing-library/jest-dom/vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { ApiClient } from '@/api/client'
import { CurrentMatchPage } from './CurrentMatchPage'

describe('CurrentMatchPage', () => {
  it('shows the empty state without loading match-dependent data when no match is active', async () => {
    const api = {
      getCurrentMatch: vi.fn().mockResolvedValue(null),
      getLeaderboard: vi.fn().mockResolvedValue({ entries: [], roundWinner: null }),
      getMatchHistory: vi.fn().mockResolvedValue([]),
      getPublicPredictions: vi.fn(),
      getUserPrediction: vi.fn(),
    } as unknown as ApiClient
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    render(
      <QueryClientProvider client={queryClient}>
        <CurrentMatchPage api={api} />
      </QueryClientProvider>,
    )

    expect(await screen.findByText('Nenhum bolao ativo no momento')).toBeVisible()
    await waitFor(() => expect(api.getLeaderboard).toHaveBeenCalledOnce())
    expect(api.getMatchHistory).toHaveBeenCalledOnce()
    expect(api.getPublicPredictions).not.toHaveBeenCalled()
    expect(api.getUserPrediction).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: /salvar palpite/i })).toBeNull()
  })
})
