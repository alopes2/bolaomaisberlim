import type { AuthClient } from '@/auth/cognito'

export type ProfileResponse = {
  publicName: string
  suffix: string | null
}

export type MatchResponse = {
  id: string
  kickoff: string
  homeTeamFifaCode: string
  awayTeamFifaCode: string
}

export type PredictionAnswers = {
  homeGoals: number
  awayGoals: number
  firstScorerKey: string
  homeTopScorerKey: string
  awayTopScorerKey: string
  homeYellowCards: number
  awayYellowCards: number
  homeRedCards: number
  awayRedCards: number
}

export type StoredPrediction = {
  submittedAt: string
}

export type LeaderboardEntry = {
  position: number
  publicName: string
  totalPoints: number
  exactScoreCount: number
  firstScorerCount: number
}

export type RoundWinnerResponse = {
  publicName: string
  points: number
}

export type LeaderboardResponse = {
  entries: LeaderboardEntry[]
  roundWinner: RoundWinnerResponse | null
}

export interface ProfileApi {
  saveProfile(givenName: string, familyName: string): Promise<ProfileResponse>
}

export class ApiClient implements ProfileApi {
  private readonly baseUrl: string
  private readonly auth: AuthClient

  constructor(baseUrl: string, auth: AuthClient) {
    this.baseUrl = baseUrl
    this.auth = auth
  }

  async saveProfile(givenName: string, familyName: string) {
    const response = await this.authorizedFetch('/me/profile', {
      method: 'PUT',
      body: JSON.stringify({ givenName, familyName }),
    })

    if (!response.ok) {
      throw new Error('Não foi possível salvar seu perfil.')
    }

    return (await response.json()) as ProfileResponse
  }

  async getCurrentMatch() {
    const response = await fetch(`${this.baseUrl.replace(/\/$/, '')}/matches/current`)
    if (!response.ok) throw new Error('Não foi possível carregar o jogo atual.')
    return (await response.json()) as MatchResponse
  }

  async getLeaderboard() {
    const response = await fetch(`${this.baseUrl.replace(/\/$/, '')}/leaderboard`)
    if (!response.ok) throw new Error('Não foi possível carregar o ranking.')
    return (await response.json()) as LeaderboardResponse
  }

  async getMatchHistory() {
    const response = await fetch(`${this.baseUrl.replace(/\/$/, '')}/matches/history`)
    if (!response.ok) throw new Error('Não foi possível carregar o histórico.')
    return (await response.json()) as MatchResponse[]
  }

  async savePrediction(matchId: string, prediction: PredictionAnswers) {
    const path = `/matches/${matchId}/prediction`
    const saved = await this.authorizedFetch(path, {
      method: 'PUT',
      body: JSON.stringify(prediction),
    })
    if (!saved.ok) throw new Error('Não foi possível salvar seu palpite.')

    const response = await this.authorizedFetch(path)
    if (!response.ok) throw new Error('O palpite foi salvo, mas não pôde ser recarregado.')
    return (await response.json()) as StoredPrediction
  }

  private async authorizedFetch(path: string, init: RequestInit = {}) {
    const token = await this.auth.accessToken()
    if (!token) throw new Error('Sua sessão expirou. Entre novamente.')

    return fetch(`${this.baseUrl.replace(/\/$/, '')}${path}`, {
      ...init,
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
        ...init.headers,
      },
    })
  }
}
