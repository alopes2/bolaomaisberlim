import type { AuthClient } from '@/auth/cognito';

export type ProfileResponse = {
  publicName: string;
  suffix: string | null;
};

export type MatchResponse = {
  id: string;
  kickoff: string;
  homeTeamFifaCode: string;
  awayTeamFifaCode: string;
};

export type MatchStatus = 'Active' | 'Upcoming' | 'Closed' | 'Archived';

export type AdminMatch = {
  id: string;
  kickoff: string;
  homeTeamFifaCode: string;
  awayTeamFifaCode: string;
  status: MatchStatus;
  resultConfirmed: boolean;
};

export type AdminMatchesResponse = {
  matches: AdminMatch[];
};

export type CreateAdminMatchRequest = {
  id: string;
  kickoff: string;
  homeTeamFifaCode: string;
  awayTeamFifaCode: string;
  prizeHandedOverAt: string | null;
};

export type UpdateAdminMatchRequest = Omit<CreateAdminMatchRequest, 'id'>;

export type PredictionAnswers = {
  homeGoals: number;
  awayGoals: number;
  firstScorerKey: string;
  homeTopScorerKey: string;
  awayTopScorerKey: string;
  homeYellowCards: number;
  awayYellowCards: number;
  homeRedCards: number;
  awayRedCards: number;
  penaltyWinnerTeamFifaCode: string | null;
};

export type StoredPrediction = {
  matchId: string;
  participantId: string;
  answers: PredictionAnswers;
  submittedAt: string;
};

export type PublicPrediction = {
  publicName: string;
  answers: PredictionAnswers;
};

export type LeaderboardEntry = {
  position: number;
  publicName: string;
  totalPoints: number;
  exactScoreCount: number;
  firstScorerCount: number;
};

export type RoundWinnerResponse = {
  publicName: string;
  points: number;
};

export type LeaderboardResponse = {
  entries: LeaderboardEntry[];
  roundWinner: RoundWinnerResponse | null;
};

export type ConfirmedResultResponse = {
  homeGoals: number;
  awayGoals: number;
  firstScorerKey: string | null;
  homeTopScorerKeys: string[];
  awayTopScorerKeys: string[];
  homeYellowCards: number;
  awayYellowCards: number;
  homeRedCards: number;
  awayRedCards: number;
};

export type ManualGoal = {
  teamFifaCode: string;
  playerKey: string;
};

export type ManualResultDraft = {
  goals: ManualGoal[];
  homeYellowCards: number;
  awayYellowCards: number;
  homeRedCards: number;
  awayRedCards: number;
  penaltyWinnerTeamFifaCode: string | null;
};

export type FinishMatchResponse = {
  closedMatchId: string;
  activatedMatchId: string | null;
};

export interface AdminApi {
  getAdminMatches(): Promise<AdminMatchesResponse>;
  createAdminMatch(request: CreateAdminMatchRequest): Promise<void>;
  updateAdminMatch(matchId: string, request: UpdateAdminMatchRequest): Promise<void>;
  getAdminResult(matchId: string): Promise<ManualResultDraft>;
  getProvisionalLeaderboard(matchId: string): Promise<LeaderboardResponse>;
  saveAdminResult(matchId: string, result: ManualResultDraft): Promise<void>;
  confirmResult(matchId: string): Promise<void>;
  finishMatch(matchId: string): Promise<FinishMatchResponse>;
}

export interface ProfileApi {
  saveProfile(givenName: string, familyName: string): Promise<ProfileResponse>;
}

export class ApiClient implements ProfileApi, AdminApi {
  private readonly baseUrl: string;
  private readonly auth: AuthClient;

  constructor(baseUrl: string, auth: AuthClient) {
    this.baseUrl = baseUrl;
    this.auth = auth;
  }

  async saveProfile(givenName: string, familyName: string) {
    const response = await this.authorizedFetch('/me/profile', {
      method: 'PUT',
      body: JSON.stringify({ givenName, familyName }),
    });

    if (!response.ok) {
      throw new Error('Não foi possível salvar seu perfil.');
    }

    return (await response.json()) as ProfileResponse;
  }

  async hasProfile() {
    const response = await this.authorizedFetch('/me/profile');
    if (!response.ok) throw new Error('Não foi possível verificar seu perfil.');
    return ((await response.json()) as { exists: boolean }).exists;
  }

  async getCurrentMatch(): Promise<MatchResponse | null> {
    const response = await fetch(
      `${this.baseUrl.replace(/\/$/, '')}/matches/current`,
    );
    if (!response.ok)
      throw new Error('Não foi possível carregar o jogo atual.');
    return (await response.json()) as MatchResponse | null;
  }

  async getLeaderboard() {
    const response = await fetch(
      `${this.baseUrl.replace(/\/$/, '')}/leaderboard`,
    );
    if (!response.ok) throw new Error('Não foi possível carregar o ranking.');
    return (await response.json()) as LeaderboardResponse;
  }

  async getMatchHistory() {
    const response = await fetch(
      `${this.baseUrl.replace(/\/$/, '')}/matches/history`,
    );
    if (!response.ok) throw new Error('Não foi possível carregar o histórico.');
    return (await response.json()) as MatchResponse[];
  }

  async getPublicPredictions(matchId: string) {
    const response = await fetch(
      `${this.baseUrl.replace(/\/$/, '')}/matches/${matchId}/predictions`,
    );
    if (!response.ok)
      throw new Error('Os palpites ainda não estão disponíveis.');
    return (await response.json()) as PublicPrediction[];
  }

  async getUserPrediction(matchId: string) {
    const path = `/matches/${matchId}/prediction`;

    const response = await this.authorizedFetch(path);
    if (!response.ok) {
      if (response.status === 404) return null;
      if (response.status === 403)
        throw new Error('Não foi possível carregar seu palpite');
    }
    return (await response.json()) as StoredPrediction;
  }

  async savePrediction(matchId: string, prediction: PredictionAnswers) {
    const path = `/matches/${matchId}/prediction`;
    const saved = await this.authorizedFetch(path, {
      method: 'PUT',
      body: JSON.stringify(prediction),
    });
    if (!saved.ok) throw new Error('Não foi possível salvar seu palpite.');

    const response = await this.authorizedFetch(path);
    if (!response.ok)
      throw new Error('O palpite foi salvo, mas não pôde ser recarregado.');
    return (await response.json()) as StoredPrediction;
  }

  async getAdminResult(matchId: string) {
    const response = await this.authorizedFetch(
      `/admin/matches/${matchId}/result`,
    );
    if (!response.ok)
      throw new Error('Não foi possível carregar o resultado provisório.');
    return (await response.json()) as ManualResultDraft;
  }

  async getAdminMatches() {
    const response = await this.authorizedFetch('/admin/matches');
    if (!response.ok) {
      throw await apiError(response, 'Não foi possível carregar os jogos.');
    }
    return (await response.json()) as AdminMatchesResponse;
  }

  async createAdminMatch(request: CreateAdminMatchRequest) {
    const response = await this.authorizedFetch('/admin/matches', {
      method: 'POST',
      body: JSON.stringify(request),
    });
    if (!response.ok) {
      throw await apiError(response, 'Não foi possível adicionar o jogo.');
    }
  }

  async updateAdminMatch(matchId: string, request: UpdateAdminMatchRequest) {
    const response = await this.authorizedFetch(
      `/admin/matches/${encodeURIComponent(matchId)}`,
      {
        method: 'PUT',
        body: JSON.stringify(request),
      },
    );
    if (!response.ok) {
      throw await apiError(response, 'Não foi possível atualizar o jogo.');
    }
  }

  async getProvisionalLeaderboard(matchId: string) {
    const response = await this.authorizedFetch(
      `/admin/matches/${matchId}/provisional-leaderboard`,
    );
    if (!response.ok)
      throw new Error('Não foi possível carregar o ranking provisório.');
    return (await response.json()) as LeaderboardResponse;
  }

  async saveAdminResult(matchId: string, result: ManualResultDraft) {
    const response = await this.authorizedFetch(
      `/admin/matches/${matchId}/result`,
      {
        method: 'PUT',
        body: JSON.stringify(result),
      },
    );
    if (!response.ok) throw new Error('Não foi possível salvar o resultado.');
  }

  async confirmResult(matchId: string) {
    const response = await this.authorizedFetch(
      `/admin/matches/${matchId}/confirm`,
      {
        method: 'POST',
      },
    );
    if (!response.ok)
      throw new Error('Não foi possível confirmar o resultado.');
  }

  async finishMatch(matchId: string) {
    const response = await this.authorizedFetch(
      `/admin/matches/${matchId}/finish`,
      { method: 'POST' },
    );
    if (!response.ok)
      throw await apiError(response, 'Não foi possível finalizar o jogo.');
    return (await response.json()) as FinishMatchResponse;
  }

  private async authorizedFetch(path: string, init: RequestInit = {}) {
    const token = await this.auth.accessToken();
    if (!token) throw new Error('Sua sessão expirou. Entre novamente.');

    return fetch(`${this.baseUrl.replace(/\/$/, '')}${path}`, {
      ...init,
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
        ...init.headers,
      },
    });
  }
}

async function apiError(response: Response, fallback: string) {
  try {
    const problem = (await response.json()) as { code?: unknown };
    if (typeof problem.code === 'string') {
      const message = adminProblemMessages[problem.code];
      if (message) return new Error(message);
    }
  } catch {
    // The API may return an empty or non-JSON gateway response.
  }
  return new Error(fallback);
}

const adminProblemMessages: Record<string, string> = {
  invalid_match: 'Revise os dados do jogo e tente novamente.',
  match_exists: 'Já existe um jogo com este ID.',
  match_not_active: 'O jogo selecionado não está ativo.',
  confirmed_result_required: 'Confirme o resultado antes de finalizar o jogo.',
  match_lifecycle_conflict: 'Outro jogo foi alterado ao mesmo tempo. Atualize a página e tente novamente.',
  match_not_found: 'Jogo não encontrado.',
};
