import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import type {
  AdminApi,
  AdminMatch,
  CreateAdminMatchRequest,
  MatchStatus,
  UpdateAdminMatchRequest,
} from '@/api/client'
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
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { berlinLocalToIso, isoToBerlinLocal } from '@/lib/berlinTime'

const statusLabels: Record<MatchStatus, string> = {
  Active: 'Ativo',
  Upcoming: 'Próximo',
  Archived: 'Arquivado',
  Closed: 'Encerrado',
}

type ManualForm = {
  id: string
  kickoff: string
  homeTeamFifaCode: string
  awayTeamFifaCode: string
}

type EditForm = Omit<ManualForm, 'id'>

const emptyForm: ManualForm = {
  id: '',
  kickoff: '',
  homeTeamFifaCode: '',
  awayTeamFifaCode: '',
}

export function AdminMatchesPage({ api }: { api: AdminApi }) {
  const queryClient = useQueryClient()
  const [form, setForm] = useState<ManualForm>(emptyForm)
  const [formValidation, setFormValidation] = useState<string | null>(null)
  const [editingMatchId, setEditingMatchId] = useState<string | null>(null)
  const [editForm, setEditForm] = useState<EditForm | null>(null)
  const [editValidation, setEditValidation] = useState<string | null>(null)
  const matchesQuery = useQuery({
    queryKey: ['admin-matches'],
    queryFn: () => api.getAdminMatches(),
  })
  const create = useMutation({
    mutationFn: (request: CreateAdminMatchRequest) => api.createAdminMatch(request),
    onSuccess: () => {
      setForm(emptyForm)
      setFormValidation(null)
      void queryClient.invalidateQueries({ queryKey: ['admin-matches'] })
    },
  })
  const finish = useMutation({
    mutationFn: (matchId: string) => api.finishMatch(matchId),
    onSuccess: () => {
      for (const queryKey of ['admin-matches', 'current-match', 'match-history', 'leaderboard']) {
        void queryClient.invalidateQueries({ queryKey: [queryKey] })
      }
    },
  })
  const update = useMutation({
    mutationFn: ({ matchId, request }: { matchId: string; request: UpdateAdminMatchRequest }) =>
      api.updateAdminMatch(matchId, request),
    onSuccess: () => {
      setEditingMatchId(null)
      setEditForm(null)
      setEditValidation(null)
      void queryClient.invalidateQueries({ queryKey: ['admin-matches'] })
      void queryClient.invalidateQueries({ queryKey: ['current-match'] })
    },
  })

  function setField(field: keyof ManualForm, value: string) {
    setForm(current => ({ ...current, [field]: value }))
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!form.id.trim() || !form.kickoff
      || !form.homeTeamFifaCode.trim() || !form.awayTeamFifaCode.trim()) {
      setFormValidation('Preencha todos os campos obrigatórios.')
      return
    }
    let kickoff: string
    try {
      kickoff = berlinLocalToIso(form.kickoff)
    } catch (error) {
      setFormValidation(errorMessage(error))
      return
    }

    setFormValidation(null)
    create.mutate({
      id: form.id.trim(),
      kickoff,
      homeTeamFifaCode: form.homeTeamFifaCode.trim().toUpperCase(),
      awayTeamFifaCode: form.awayTeamFifaCode.trim().toUpperCase(),
      prizeHandedOverAt: null,
    })
  }

  function startEditing(match: AdminMatch) {
    update.reset()
    setEditValidation(null)
    setEditingMatchId(match.id)
    setEditForm({
      kickoff: isoToBerlinLocal(match.kickoff),
      homeTeamFifaCode: match.homeTeamFifaCode,
      awayTeamFifaCode: match.awayTeamFifaCode,
    })
  }

  function cancelEditing() {
    setEditingMatchId(null)
    setEditForm(null)
    setEditValidation(null)
    update.reset()
  }

  function handleEditSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!editingMatchId || !editForm) return
    if (!editForm.kickoff || !editForm.homeTeamFifaCode.trim()
      || !editForm.awayTeamFifaCode.trim()) {
      setEditValidation('Preencha todos os campos obrigatórios.')
      return
    }
    let kickoff: string
    try {
      kickoff = berlinLocalToIso(editForm.kickoff)
    } catch (error) {
      setEditValidation(errorMessage(error))
      return
    }
    setEditValidation(null)
    update.mutate({
      matchId: editingMatchId,
      request: {
        kickoff,
        homeTeamFifaCode: editForm.homeTeamFifaCode.trim().toUpperCase(),
        awayTeamFifaCode: editForm.awayTeamFifaCode.trim().toUpperCase(),
        prizeHandedOverAt: null,
      },
    })
  }

  if (matchesQuery.isPending) {
    return <main className="mx-auto w-full max-w-3xl p-4 sm:p-8"><Skeleton className="h-96 w-full" /></main>
  }
  if (matchesQuery.isError) {
    return <main className="p-4 text-sm text-destructive" role="alert">Não foi possível carregar os jogos.</main>
  }

  const sortedMatches = [...matchesQuery.data.matches].sort((left, right) =>
    left.kickoff.localeCompare(right.kickoff) || left.id.localeCompare(right.id))

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-4 p-4 sm:p-8">
      <Card>
        <CardHeader>
          <CardTitle>Adicionar jogo manualmente</CardTitle>
          <CardDescription>Cadastre o próximo jogo e sua data de início.</CardDescription>
        </CardHeader>
        <CardContent>
          <form className="grid gap-4 sm:grid-cols-2" onSubmit={handleSubmit} noValidate>
            <Field label="ID do jogo" value={form.id} onChange={value => setField('id', value)} invalid={isInvalidField('id', form, formValidation)} />
            <Field label="Data e hora em Europe/Berlin" type="datetime-local" value={form.kickoff} onChange={value => setField('kickoff', value)} invalid={isInvalidField('kickoff', form, formValidation)} />
            <Field label="Mandante" value={form.homeTeamFifaCode} onChange={value => setField('homeTeamFifaCode', value)} invalid={isInvalidField('homeTeamFifaCode', form, formValidation)} />
            <Field label="Visitante" value={form.awayTeamFifaCode} onChange={value => setField('awayTeamFifaCode', value)} invalid={isInvalidField('awayTeamFifaCode', form, formValidation)} />
            <div className="flex flex-col items-start gap-2 sm:col-span-2">
              {formValidation ? <p id="manual-form-error" className="text-sm text-destructive" role="alert">{formValidation}</p> : null}
              {create.isError ? <p className="text-sm text-destructive" role="alert">{errorMessage(create.error)}</p> : null}
              {create.isSuccess ? <p className="text-sm" role="status">Jogo adicionado.</p> : null}
              <Button type="submit" disabled={create.isPending}>
                {create.isPending ? 'Adicionando…' : 'Adicionar jogo'}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>

      {editingMatchId && editForm ? (
        <Card>
          <CardHeader>
            <CardTitle>Editar jogo cadastrado</CardTitle>
            <CardDescription>ID do jogo: {editingMatchId}</CardDescription>
          </CardHeader>
          <CardContent>
            <form className="grid gap-4 sm:grid-cols-2" onSubmit={handleEditSubmit} noValidate>
              <Field
                label="Data e hora do jogo em Europe/Berlin"
                type="datetime-local"
                value={editForm.kickoff}
                onChange={value => setEditForm(current => current ? { ...current, kickoff: value } : current)}
                invalid={Boolean(editValidation && (!editForm.kickoff || editValidation.includes('Europe/Berlin') || editValidation === 'Data e hora inválidas.'))}
                errorId="edit-form-error"
              />
              <Field
                label="Mandante do jogo"
                value={editForm.homeTeamFifaCode}
                onChange={value => setEditForm(current => current ? { ...current, homeTeamFifaCode: value } : current)}
                invalid={Boolean(editValidation && !editForm.homeTeamFifaCode.trim())}
                errorId="edit-form-error"
              />
              <Field
                label="Visitante do jogo"
                value={editForm.awayTeamFifaCode}
                onChange={value => setEditForm(current => current ? { ...current, awayTeamFifaCode: value } : current)}
                invalid={Boolean(editValidation && !editForm.awayTeamFifaCode.trim())}
                errorId="edit-form-error"
              />
              <div className="flex flex-col items-start gap-2 sm:col-span-2">
                {editValidation ? <p id="edit-form-error" className="text-sm text-destructive" role="alert">{editValidation}</p> : null}
                {update.isError ? <p className="text-sm text-destructive" role="alert">{errorMessage(update.error)}</p> : null}
                <div className="flex gap-2">
                  <Button type="submit" disabled={update.isPending}>
                    {update.isPending ? 'Salvando…' : 'Salvar alterações'}
                  </Button>
                  <Button type="button" variant="outline" onClick={cancelEditing} disabled={update.isPending}>
                    Cancelar edição
                  </Button>
                </div>
              </div>
            </form>
          </CardContent>
        </Card>
      ) : null}

      <Card>
        <CardHeader>
          <CardTitle>Jogos</CardTitle>
          <CardDescription>{sortedMatches.length} jogos cadastrados.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {finish.data ? (
            <p className="text-sm" role="status">
              {finish.data.activatedMatchId
                ? `Jogo finalizado. Próximo jogo ativado: ${finish.data.activatedMatchId}.`
                : 'Jogo finalizado. Adicione o próximo jogo.'}
            </p>
          ) : null}
          {finish.isError ? <p className="text-sm text-destructive" role="alert">{errorMessage(finish.error)}</p> : null}
          {update.isSuccess ? <p className="text-sm" role="status">Jogo atualizado.</p> : null}
          {sortedMatches.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum jogo cadastrado.</p> : null}
          {sortedMatches.map(match => (
            <div key={match.id} className="flex flex-col gap-2 rounded-lg border p-3 sm:flex-row sm:items-center sm:justify-between">
              <div className="flex flex-col gap-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-medium">{match.homeTeamFifaCode} × {match.awayTeamFifaCode}</span>
                  {match.status ? <Badge variant="secondary">{statusLabels[match.status]}</Badge> : null}
                </div>
                <span className="text-sm text-muted-foreground">
                  {new Date(match.kickoff).toLocaleString('pt-BR')} · {match.id}
                </span>
              </div>
              <div className="flex flex-col items-start gap-2 sm:items-end">
                <Button type="button" variant="outline" size="sm" onClick={() => startEditing(match)}>
                  Editar jogo
                </Button>
                <Button asChild variant="outline" size="sm">
                  <a href={`/admin?matchId=${encodeURIComponent(match.id)}`}>Apurar resultado</a>
                </Button>
                {match.status === 'Active' ? (
                  <>
                    <AlertDialog>
                      <AlertDialogTrigger asChild>
                        <Button size="sm" disabled={!match.resultConfirmed || finish.isPending}>
                          Finalizar jogo atual
                        </Button>
                      </AlertDialogTrigger>
                      <AlertDialogContent>
                        <AlertDialogHeader>
                          <AlertDialogTitle>Finalizar o jogo atual?</AlertDialogTitle>
                          <AlertDialogDescription>
                            O jogo será encerrado e o próximo jogo cadastrado será ativado.
                          </AlertDialogDescription>
                        </AlertDialogHeader>
                        <AlertDialogFooter>
                          <AlertDialogCancel>Cancelar</AlertDialogCancel>
                          <AlertDialogAction onClick={() => finish.mutate(match.id)}>
                            Finalizar jogo
                          </AlertDialogAction>
                        </AlertDialogFooter>
                      </AlertDialogContent>
                    </AlertDialog>
                    {!match.resultConfirmed ? (
                      <p className="text-sm text-muted-foreground">
                        Confirme o resultado antes de finalizar o jogo.
                      </p>
                    ) : null}
                  </>
                ) : null}
              </div>
            </div>
          ))}
        </CardContent>
      </Card>
    </main>
  )
}

function Field({
  label,
  type = 'text',
  value,
  onChange,
  invalid = false,
  errorId = 'manual-form-error',
}: {
  label: string
  type?: string
  value: string
  onChange(value: string): void
  invalid?: boolean
  errorId?: string
}) {
  const id = label.toLowerCase().replaceAll(' ', '-')
  return (
    <div className="grid gap-2">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        type={type}
        value={value}
        aria-invalid={invalid}
        aria-describedby={invalid ? errorId : undefined}
        onChange={event => onChange(event.target.value)}
      />
    </div>
  )
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'Ocorreu um erro inesperado.'
}

function isInvalidField(field: keyof ManualForm, form: ManualForm, validation: string | null) {
  if (!validation) return false
  if (validation.includes('Europe/Berlin') || validation === 'Data e hora inválidas.') {
    return field === 'kickoff'
  }
  return !form[field].trim()
}
