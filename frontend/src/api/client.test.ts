import { describe, expect, it, vi } from 'vitest'

import { ApiClient } from './client'

describe('ApiClient', () => {
  it('reads the server submission timestamp after saving a prediction', async () => {
    const auth = {
      signIn: vi.fn(),
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

  it('uses authenticated admin match-management endpoints', async () => {
    const auth = {
      signIn: vi.fn(),
      signOut: vi.fn(),
      accessToken: vi.fn().mockResolvedValue('admin-token'),
    }
    const matches = {
      matches: [],
      lastSuccessfulSyncAt: null,
      providerCallAvailable: true,
    }
    const sync = {
      providerFetchPerformed: true,
      lastSuccessfulSyncAt: '2026-06-30T08:00:00Z',
      createdCount: 2,
      updatedCount: 1,
      statusChangeCount: 1,
      skippedFixtures: [],
    }
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json(matches))
      .mockResolvedValueOnce(Response.json(sync))
      .mockResolvedValueOnce(Response.json({}, { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)
    const api = new ApiClient('https://api.example.com/', auth)

    await expect(api.getAdminMatches()).resolves.toEqual(matches)
    await expect(api.syncWorldCupMatches()).resolves.toEqual(sync)
    await expect(api.createAdminMatch({
      id: 'wc2026-123',
      providerFixtureId: 123,
      kickoff: '2026-07-01T18:00:00Z',
      homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'ARG',
      prizeHandedOverAt: null,
    })).resolves.toBeUndefined()

    expect(fetchMock).toHaveBeenNthCalledWith(1, 'https://api.example.com/admin/matches', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer admin-token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, 'https://api.example.com/admin/matches/world-cup/sync', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({ Authorization: 'Bearer admin-token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(3, 'https://api.example.com/admin/matches', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({ Authorization: 'Bearer admin-token' }),
      body: JSON.stringify({
        id: 'wc2026-123',
        providerFixtureId: 123,
        kickoff: '2026-07-01T18:00:00Z',
        homeTeamFifaCode: 'BRA',
        awayTeamFifaCode: 'ARG',
        prizeHandedOverAt: null,
      }),
    }))
  })

  it.each([
    [400, 'invalid_match', 'Revise os dados do jogo e tente novamente.'],
    [409, 'match_exists', 'Já existe um jogo com este ID.'],
    [502, 'fixture_sync_failed', 'Não foi possível importar os jogos da Copa do Mundo. Tente novamente.'],
    [503, 'fixture_status_reconciliation_failed', 'Os jogos foram importados, mas os status não foram atualizados. Tente sincronizar novamente.'],
  ])('maps stable admin API code %s to Portuguese', async (status, code, message) => {
    const auth = {
      signIn: vi.fn(),
      signOut: vi.fn(),
      accessToken: vi.fn().mockResolvedValue('admin-token'),
    }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(
      { code, detail: 'Sensitive backend detail.' },
      { status },
    )))
    const api = new ApiClient('https://api.example.com', auth)

    await expect(status >= 500
      ? api.syncWorldCupMatches()
      : api.createAdminMatch({
        id: 'wc2026-123',
        providerFixtureId: 123,
        kickoff: '2026-07-01T18:00:00Z',
        homeTeamFifaCode: 'BRA',
        awayTeamFifaCode: 'ARG',
        prizeHandedOverAt: null,
      })).rejects.toThrow(message)
  })

  it('does not expose unknown backend problem details', async () => {
    const auth = {
      signIn: vi.fn(),
      signOut: vi.fn(),
      accessToken: vi.fn().mockResolvedValue('admin-token'),
    }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(
      { code: 'unknown', detail: 'Internal database details.' },
      { status: 500 },
    )))
    const api = new ApiClient('https://api.example.com', auth)

    await expect(api.syncWorldCupMatches()).rejects.toThrow('Não foi possível sincronizar os jogos.')
  })
})
