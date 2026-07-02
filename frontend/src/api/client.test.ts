import { describe, expect, it, vi } from 'vitest'

import { ApiClient, type ManualResultDraft } from './client'

function auth(token = 'token') {
  return {
    signIn: vi.fn(),
    signOut: vi.fn(),
    accessToken: vi.fn().mockResolvedValue(token),
  }
}

describe('ApiClient', () => {
  it('returns null when there is no current match', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(null)))
    const api = new ApiClient('https://api.example.com/', auth())

    await expect(api.getCurrentMatch()).resolves.toBeNull()
  })

  it('serializes a penalty winner when saving a prediction', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(
        Response.json({ submittedAt: '2026-06-29T15:30:00Z' }),
      )
    vi.stubGlobal('fetch', fetchMock)
    const api = new ApiClient('https://api.example.com', auth())

    const result = await api.savePrediction('match-1', {
      homeGoals: 1,
      awayGoals: 1,
      firstScorerKey: 'BRA:7',
      homeTopScorerKey: 'BRA:7',
      awayTopScorerKey: 'GER:10',
      homeYellowCards: 1,
      awayYellowCards: 2,
      homeRedCards: 0,
      awayRedCards: 0,
      penaltyWinnerTeamFifaCode: 'BRA',
    })

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      'https://api.example.com/matches/match-1/prediction',
      expect.objectContaining({
        method: 'PUT',
        body: expect.stringContaining('"penaltyWinnerTeamFifaCode":"BRA"'),
      }),
    )
    expect(result.submittedAt).toBe('2026-06-29T15:30:00Z')
  })

  it('lists and creates provider-free admin matches', async () => {
    const matches = {
      matches: [{
        id: 'wc2026-123',
        kickoff: '2026-07-01T18:00:00Z',
        homeTeamFifaCode: 'BRA',
        awayTeamFifaCode: 'ARG',
        status: 'Active',
        resultConfirmed: false,
      }],
    }
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json(matches))
      .mockResolvedValueOnce(Response.json({}, { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)
    const api = new ApiClient('https://api.example.com/', auth('admin-token'))

    await expect(api.getAdminMatches()).resolves.toEqual(matches)
    await expect(api.createAdminMatch({
      id: 'wc2026-123',
      kickoff: '2026-07-01T18:00:00Z',
      homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'ARG',
      prizeHandedOverAt: null,
    })).resolves.toBeUndefined()

    expect('syncWorldCupMatches' in api).toBe(false)
    expect(fetchMock).toHaveBeenNthCalledWith(2, 'https://api.example.com/admin/matches', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({
        id: 'wc2026-123',
        kickoff: '2026-07-01T18:00:00Z',
        homeTeamFifaCode: 'BRA',
        awayTeamFifaCode: 'ARG',
        prizeHandedOverAt: null,
      }),
    }))
  })

  it('updates editable match data without sending a replacement ID', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    const api = new ApiClient('https://api.example.com/', auth('admin-token'))

    await expect(api.updateAdminMatch('wc2026-123', {
      kickoff: '2026-07-02T18:00:00Z',
      homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'FRA',
      prizeHandedOverAt: null,
    })).resolves.toBeUndefined()

    expect(fetchMock).toHaveBeenCalledWith(
      'https://api.example.com/admin/matches/wc2026-123',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          kickoff: '2026-07-02T18:00:00Z',
          homeTeamFifaCode: 'BRA',
          awayTeamFifaCode: 'FRA',
          prizeHandedOverAt: null,
        }),
      }),
    )
  })

  it('maps stable match update errors to Portuguese', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(
      { code: 'match_not_found', detail: 'Sensitive backend detail.' },
      { status: 404 },
    )))
    const api = new ApiClient('https://api.example.com', auth('admin-token'))

    await expect(api.updateAdminMatch('missing', {
      kickoff: '2026-07-02T18:00:00Z',
      homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'FRA',
      prizeHandedOverAt: null,
    })).rejects.toThrow('Jogo não encontrado.')
  })

  it('loads and saves an ordered manual result', async () => {
    const result: ManualResultDraft = {
      goals: [
        { teamFifaCode: 'BRA', playerKey: 'BRA:7' },
        { teamFifaCode: 'ARG', playerKey: 'ARG:10' },
      ],
      homeYellowCards: 1,
      awayYellowCards: 2,
      homeRedCards: 0,
      awayRedCards: 0,
      penaltyWinnerTeamFifaCode: 'BRA',
    }
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json(result))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    const api = new ApiClient('https://api.example.com', auth('admin-token'))

    await expect(api.getAdminResult('match-1')).resolves.toEqual(result)
    await expect(api.saveAdminResult('match-1', result)).resolves.toBeUndefined()

    expect(fetchMock).toHaveBeenNthCalledWith(1, 'https://api.example.com/admin/matches/match-1/result', expect.any(Object))
    expect(fetchMock).toHaveBeenNthCalledWith(2, 'https://api.example.com/admin/matches/match-1/result', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify(result),
    }))
  })

  it('finishes a match and returns the activated match', async () => {
    const response = { closedMatchId: 'match-1', activatedMatchId: 'match-2' }
    const fetchMock = vi.fn().mockResolvedValue(Response.json(response))
    vi.stubGlobal('fetch', fetchMock)
    const api = new ApiClient('https://api.example.com', auth('admin-token'))

    await expect(api.finishMatch('match-1')).resolves.toEqual(response)
    expect(fetchMock).toHaveBeenCalledWith(
      'https://api.example.com/admin/matches/match-1/finish',
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it.each([
    ['match_not_active', 'O jogo selecionado não está ativo.'],
    ['confirmed_result_required', 'Confirme o resultado antes de finalizar o jogo.'],
    ['match_lifecycle_conflict', 'Outro jogo foi alterado ao mesmo tempo. Atualize a página e tente novamente.'],
    ['match_not_found', 'Jogo não encontrado.'],
  ])('maps finish error %s to Portuguese', async (code, message) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(
      { code, detail: 'Sensitive backend detail.' },
      { status: code === 'match_not_found' ? 404 : 409 },
    )))
    const api = new ApiClient('https://api.example.com', auth('admin-token'))

    await expect(api.finishMatch('match-1')).rejects.toThrow(message)
  })

  it.each([
    [400, 'invalid_match', 'Revise os dados do jogo e tente novamente.'],
    [409, 'match_exists', 'Já existe um jogo com este ID.'],
  ])('maps stable admin API code %s to Portuguese', async (status, code, message) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(
      { code, detail: 'Sensitive backend detail.' },
      { status },
    )))
    const api = new ApiClient('https://api.example.com', auth('admin-token'))

    await expect(api.createAdminMatch({
      id: 'wc2026-123',
      kickoff: '2026-07-01T18:00:00Z',
      homeTeamFifaCode: 'BRA',
      awayTeamFifaCode: 'ARG',
      prizeHandedOverAt: null,
    })).rejects.toThrow(message)
  })
})
