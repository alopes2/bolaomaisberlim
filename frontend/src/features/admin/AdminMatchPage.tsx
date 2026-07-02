import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import type { AdminApi } from '@/api/client'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'

import { ProvisionalLeaderboard } from './ProvisionalLeaderboard'
import { ResultEditor } from './ResultEditor'

type ResultAdminApi = Pick<AdminApi,
  'getAdminMatches' | 'getAdminResult' | 'getProvisionalLeaderboard' | 'saveAdminResult' | 'confirmResult'>

export function AdminMatchPage({ api, matchId }: { api: ResultAdminApi; matchId: string }) {
  const queryClient = useQueryClient()
  const [editorDirty, setEditorDirty] = useState(false)
  const matchesQuery = useQuery({
    queryKey: ['admin-matches'],
    queryFn: () => api.getAdminMatches(),
  })
  const resultQuery = useQuery({
    queryKey: ['admin-result', matchId],
    queryFn: () => api.getAdminResult(matchId),
  })
  const leaderboardQuery = useQuery({
    queryKey: ['admin-leaderboard', matchId],
    queryFn: () => api.getProvisionalLeaderboard(matchId),
  })
  const save = useMutation({
    mutationFn: (result: NonNullable<typeof resultQuery.data>) =>
      api.saveAdminResult(matchId, result),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['admin-result', matchId] }),
        queryClient.invalidateQueries({ queryKey: ['admin-leaderboard', matchId] }),
      ])
    },
  })
  const confirm = useMutation({
    mutationFn: () => api.confirmResult(matchId),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['admin-matches'] }),
        queryClient.invalidateQueries({ queryKey: ['admin-leaderboard', matchId] }),
      ])
    },
  })

  if (matchesQuery.isPending || resultQuery.isPending || leaderboardQuery.isPending) {
    return <main className="mx-auto w-full max-w-3xl p-4"><Skeleton className="h-80 w-full" /></main>
  }
  if (matchesQuery.isError || resultQuery.isError || leaderboardQuery.isError) {
    return <main className="p-4 text-sm text-destructive">Não foi possível carregar a administração.</main>
  }

  const match = matchesQuery.data.matches.find((candidate) => candidate.id === matchId)
  if (!match) {
    return <main className="p-4 text-sm text-destructive">Jogo não encontrado.</main>
  }

  const draft = resultQuery.data

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-3xl flex-col gap-4 p-4 sm:p-8">
      <Card>
        <CardHeader>
          <CardTitle>Apuração do jogo</CardTitle>
          <CardDescription>
            {match.homeTeamFifaCode} × {match.awayTeamFifaCode}. Revise o resultado informado manualmente antes de publicar os pontos.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-5">
          <ResultEditor
            value={draft}
            homeTeamFifaCode={match.homeTeamFifaCode}
            awayTeamFifaCode={match.awayTeamFifaCode}
            saving={save.isPending}
            onDirtyChange={setEditorDirty}
            onSave={async (result) => {
              await save.mutateAsync(result)
              setEditorDirty(false)
            }}
          />
          {save.isError ? (
            <p role="alert" className="text-sm text-destructive">Não foi possível salvar o resultado. Tente novamente.</p>
          ) : null}
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button disabled={editorDirty || save.isPending || confirm.isPending}>Confirmar resultado</Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>Publicar resultado e ranking?</AlertDialogTitle>
                <AlertDialogDescription>
                  Esta ação calcula os pontos, publica o ranking e inicia a notificação do vencedor.
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>Cancelar</AlertDialogCancel>
                <AlertDialogAction onClick={() => confirm.mutate()}>
                  Confirmar
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
          {confirm.isError ? (
            <p role="alert" className="text-sm text-destructive">Não foi possível confirmar o resultado. Tente novamente.</p>
          ) : null}
        </CardContent>
      </Card>
      <ProvisionalLeaderboard leaderboard={leaderboardQuery.data} />
    </main>
  )
}
