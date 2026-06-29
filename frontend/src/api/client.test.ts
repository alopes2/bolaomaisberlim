import { describe, expect, it, vi } from 'vitest'

import { ApiClient } from './client'

describe('ApiClient', () => {
  it('reads the server submission timestamp after saving a prediction', async () => {
    const auth = {
      start: vi.fn(),
      confirm: vi.fn(),
      signOut: vi.fn(),
      accessToken: vi.fn().mockResolvedValue('token'),
    }
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(
        Response.json({ submittedAt: '2026-06-29T15:30:00Z' }),
      )
    vi.stubGlobal('fetch', fetchMock)
    const api = new ApiClient('https://api.example.com', auth)

    const result = await api.savePrediction('match-1', {
      homeGoals: 2,
      awayGoals: 1,
      firstScorerKey: 'BRA:7',
      homeTopScorerKey: 'BRA:7',
      awayTopScorerKey: 'GER:10',
      homeYellowCards: 1,
      awayYellowCards: 2,
      homeRedCards: 0,
      awayRedCards: 0,
    })

    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      'https://api.example.com/matches/match-1/prediction',
      expect.objectContaining({
        headers: expect.objectContaining({ Authorization: 'Bearer token' }),
      }),
    )
    expect(result.submittedAt).toBe('2026-06-29T15:30:00Z')
  })
})
