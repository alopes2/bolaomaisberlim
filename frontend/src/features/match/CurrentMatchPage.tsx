import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';

import type { ApiClient } from '@/api/client';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { getRoster } from '@/data/rosters';
import { Leaderboard } from '@/features/leaderboard/Leaderboard';
import { MatchHistory } from '@/features/leaderboard/MatchHistory';
import { RoundWinner } from '@/features/leaderboard/RoundWinner';
import { Skeleton } from '@/components/ui/skeleton';

import { PredictionForm, type PredictionValues } from './PredictionForm';

export function CurrentMatchPage({ api }: { api: ApiClient }) {
  const [submittedAt, setSubmittedAt] = useState<string | null>(null);
  const matchQuery = useQuery({
    queryKey: ['current-match'],
    queryFn: () => api.getCurrentMatch(),
  });
  const leaderboardQuery = useQuery({
    queryKey: ['leaderboard'],
    queryFn: () => api.getLeaderboard(),
  });
  const historyQuery = useQuery({
    queryKey: ['match-history'],
    queryFn: () => api.getMatchHistory(),
  });
  const publicPredictionsQuery = useQuery({
    queryKey: ['public-predictions', matchQuery.data?.id],
    queryFn: () => api.getPublicPredictions(matchQuery.data!.id),
    enabled: Boolean(matchQuery.data),
    retry: false,
  });

  const userPredictionQuery = useQuery({
    queryKey: ['user-prediction', matchQuery.data?.id],
    queryFn: () => api.getUserPrediction(matchQuery.data!.id),
    enabled: Boolean(matchQuery.data),
    retry: false,
  });

  const savePrediction = useMutation({
    mutationFn: ({
      matchId,
      prediction,
    }: {
      matchId: string;
      prediction: PredictionValues;
    }) => api.savePrediction(matchId, prediction),
    onSuccess: (saved) => setSubmittedAt(saved.submittedAt),
  });

  if (matchQuery.isPending) {
    return (
      <main className="p-4 text-sm text-muted-foreground">
        Carregando jogo…
      </main>
    );
  }

  if (matchQuery.isError) {
    return (
      <main className="p-4 text-sm text-destructive">
        {matchQuery.error.message}
      </main>
    );
  }

  const match = matchQuery.data;
  const home = getRoster(match.homeTeamFifaCode);
  const away = getRoster(match.awayTeamFifaCode);

  const cutoffAt = new Date(
    new Date(match.kickoff).getTime() - 10 * 60 * 1_000,
  ).toISOString();

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-2xl flex-col gap-4 p-4 sm:p-8">
      <Card>
        <CardHeader>
          <CardTitle>
            {home.flagIcon} {home.name} × {away.name} {away.flagIcon}
          </CardTitle>
          <CardDescription>
            {new Date(match.kickoff).toLocaleString('pt-BR')} · palpites até 10
            minutos antes
          </CardDescription>
        </CardHeader>
        <CardContent>
          {userPredictionQuery.isPending ? (
            <Skeleton className="h-32 w-full" />
          ) : userPredictionQuery.isError ? (
            <p className="text-sm text-destructive">
              {userPredictionQuery.error.message}
            </p>
          ) : (
            <PredictionForm
              homeTeam={home.name}
              awayTeam={away.name}
              homePlayers={home.players}
              awayPlayers={away.players}
              cutoffAt={cutoffAt}
              submittedAt={submittedAt}
              storedPrediction={userPredictionQuery.data?.answers}
              onSubmit={async (prediction) => {
                await savePrediction.mutateAsync({
                  matchId: match.id,
                  prediction,
                });
              }}
            />
          )}

          {savePrediction.isError ? (
            <p className="mt-3 text-sm text-destructive">
              {savePrediction.error.message}
            </p>
          ) : null}
        </CardContent>
      </Card>
      {publicPredictionsQuery.isSuccess ? (
        <Card>
          <CardHeader>
            <CardTitle>Palpites da comunidade</CardTitle>
            <CardDescription>
              Disponíveis depois do encerramento dos envios.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="flex flex-col gap-2">
              {publicPredictionsQuery.data.map((prediction) => (
                <li key={prediction.publicName} className="text-sm">
                  <strong>{prediction.publicName}</strong>:{' '}
                  {prediction.answers.homeGoals} ×{' '}
                  {prediction.answers.awayGoals}
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      ) : null}
      {leaderboardQuery.isPending ? (
        <Skeleton className="h-32 w-full" />
      ) : leaderboardQuery.isSuccess ? (
        <>
          <RoundWinner winner={leaderboardQuery.data.roundWinner} />
          <Leaderboard entries={leaderboardQuery.data.entries} />
        </>
      ) : null}
      {historyQuery.isPending ? (
        <Skeleton className="h-24 w-full" />
      ) : historyQuery.isSuccess ? (
        <MatchHistory matches={historyQuery.data} />
      ) : null}
    </main>
  );
}
