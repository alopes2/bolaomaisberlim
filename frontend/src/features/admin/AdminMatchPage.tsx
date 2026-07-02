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
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'

import { ProvisionalLeaderboard } from './ProvisionalLeaderboard'
import { ResultEditor } from './ResultEditor'

type ResultAdminApi = Pick<AdminApi,
  'getAdminResult' | 'getProvisionalLeaderboard' | 'saveAdminResult' | 'confirmResult'>

export function AdminMatchPage({ api, matchId }: { api: ResultAdminApi; matchId: string }) {
  const queryClient = useQueryClient()
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
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-result', matchId] }),
  })
  const confirm = useMutation({ mutationFn: () => api.confirmResult(matchId) })

  if (resultQuery.isPending || leaderboardQuery.isPending) {
    return <main className="mx-auto w-full max-w-3xl p-4"><Skeleton className="h-80 w-full" /></main>
  }
  if (resultQuery.isError || leaderboardQuery.isError) {
    return <main className="p-4 text-sm text-destructive">Não foi possível carregar a administração.</main>
  }

  const raw = resultQuery.data
  const goalsValid = (raw.homeGoalEvents === null || raw.homeGoalEvents === raw.result.homeGoals)
    && (raw.awayGoalEvents === null || raw.awayGoalEvents === raw.result.awayGoals)
  const canConfirm = raw.unresolvedPlayers.length === 0 && goalsValid && !confirm.isPending

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-3xl flex-col gap-4 p-4 sm:p-8">
      <Card>
        <CardHeader>
          <CardTitle>Apuração do jogo</CardTitle>
          <CardDescription>Revise os dados da API antes de publicar os pontos.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-5">
          <div className="flex items-center gap-2">
            <span className="text-sm text-muted-foreground">Status do provedor</span>
            <Badge>{raw.providerStatus}</Badge>
          </div>
          <ResultEditor value={raw} saving={save.isPending} onSave={save.mutateAsync} />
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button disabled={!canConfirm}>Confirmar resultado</Button>
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
                <AlertDialogAction onClick={() => confirm.mutate()}>Confirmar</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </CardContent>
      </Card>
      <ProvisionalLeaderboard leaderboard={leaderboardQuery.data} />
    </main>
  )
}
