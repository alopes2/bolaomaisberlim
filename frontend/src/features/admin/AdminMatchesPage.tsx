import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import type {
  AdminApi,
  CreateAdminMatchRequest,
  MatchStatus,
  WorldCupSkipReasonCode,
  WorldCupSyncResponse,
} from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { berlinLocalToIso } from '@/lib/berlinTime'

const statusLabels: Record<MatchStatus, string> = {
  Active: 'Ativo',
  Upcoming: 'Próximo',
  Archived: 'Arquivado',
  Closed: 'Encerrado',
}

type ManualForm = {
  id: string
  providerFixtureId: string
  kickoff: string
  homeTeamFifaCode: string
  awayTeamFifaCode: string
}

const emptyForm: ManualForm = {
  id: '',
  providerFixtureId: '',
  kickoff: '',
  homeTeamFifaCode: '',
  awayTeamFifaCode: '',
}

export function AdminMatchesPage({ api }: { api: AdminApi }) {
  const queryClient = useQueryClient()
  const [form, setForm] = useState<ManualForm>(emptyForm)
  const [formValidation, setFormValidation] = useState<string | null>(null)
  const matchesQuery = useQuery({
    queryKey: ['admin-matches'],
    queryFn: () => api.getAdminMatches(),
  })
  const sync = useMutation({
    mutationFn: () => api.syncWorldCupMatches(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-matches'] }),
  })
  const create = useMutation({
    mutationFn: (request: CreateAdminMatchRequest) => api.createAdminMatch(request),
    onSuccess: () => {
      setForm(emptyForm)
      setFormValidation(null)
      void queryClient.invalidateQueries({ queryKey: ['admin-matches'] })
    },
  })

  function setField(field: keyof ManualForm, value: string) {
    setForm(current => ({ ...current, [field]: value }))
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const fixtureId = Number(form.providerFixtureId)
    if (!form.id.trim() || !form.providerFixtureId || !form.kickoff
      || !form.homeTeamFifaCode.trim() || !form.awayTeamFifaCode.trim()) {
      setFormValidation('Preencha todos os campos obrigatórios.')
      return
    }
    if (!Number.isInteger(fixtureId) || fixtureId <= 0) {
      setFormValidation('Informe um ID de fixture inteiro e positivo.')
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
      providerFixtureId: fixtureId,
      kickoff,
      homeTeamFifaCode: form.homeTeamFifaCode.trim().toUpperCase(),
      awayTeamFifaCode: form.awayTeamFifaCode.trim().toUpperCase(),
      prizeHandedOverAt: null,
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
  const providerCallAvailable = matchesQuery.data.providerCallAvailable
  const lastSuccessfulSyncAt = matchesQuery.data.lastSuccessfulSyncAt

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-4 p-4 sm:p-8">
      <Card>
        <CardHeader>
          <CardTitle>Sincronizar Copa do Mundo</CardTitle>
          <CardDescription>
            {providerCallAvailable
              ? 'A próxima sincronização consultará o API-Football e recalculará os status.'
              : 'A próxima sincronização hoje apenas recalculará os status, sem consultar o API-Football.'}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col items-start gap-3">
          {lastSuccessfulSyncAt ? (
            <p className="text-sm text-muted-foreground" role="status">
              Última sincronização bem-sucedida: {new Date(lastSuccessfulSyncAt).toLocaleString('pt-BR')}
            </p>
          ) : null}
          <Button onClick={() => sync.mutate()} disabled={sync.isPending}>
            {sync.isPending ? 'Sincronizando…' : 'Sincronizar jogos'}
          </Button>
          {sync.data ? <SyncFeedback result={sync.data} /> : null}
          {sync.isError ? <p className="text-sm text-destructive" role="alert">{errorMessage(sync.error)}</p> : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Adicionar jogo manualmente</CardTitle>
          <CardDescription>Use esta opção quando o fixture não estiver disponível na sincronização.</CardDescription>
        </CardHeader>
        <CardContent>
          <form className="grid gap-4 sm:grid-cols-2" onSubmit={handleSubmit} noValidate>
            <Field label="ID do jogo" value={form.id} onChange={value => setField('id', value)} invalid={isInvalidField('id', form, formValidation)} />
            <Field label="ID do fixture" type="number" value={form.providerFixtureId} onChange={value => setField('providerFixtureId', value)} invalid={isInvalidField('providerFixtureId', form, formValidation)} />
            <Field label="Data e hora em Europe/Berlin" type="datetime-local" value={form.kickoff} onChange={value => setField('kickoff', value)} invalid={isInvalidField('kickoff', form, formValidation)} />
            <div />
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

      <Card>
        <CardHeader>
          <CardTitle>Jogos</CardTitle>
          <CardDescription>{sortedMatches.length} jogos cadastrados.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
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
              <Button asChild variant="outline" size="sm">
                <a href={`/admin?matchId=${encodeURIComponent(match.id)}`}>Apurar resultado</a>
              </Button>
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
}: {
  label: string
  type?: string
  value: string
  onChange(value: string): void
  invalid?: boolean
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
        aria-describedby={invalid ? 'manual-form-error' : undefined}
        onChange={event => onChange(event.target.value)}
      />
    </div>
  )
}

function SyncFeedback({ result }: { result: WorldCupSyncResponse }) {
  return (
    <div className="text-sm" role="status">
      <p>
        {result.providerFetchPerformed
          ? `${result.createdCount} criados, ${result.updatedCount} atualizados e ${result.statusChangeCount} status alterado${result.statusChangeCount === 1 ? '' : 's'}.`
          : `Nenhuma consulta ao API-Football foi feita; ${result.statusChangeCount} status alterado${result.statusChangeCount === 1 ? '' : 's'}.`}
      </p>
      {result.skippedFixtures.map(fixture => (
        <p key={fixture.fixtureId} className="text-muted-foreground">
          Fixture {fixture.fixtureId}: {skipReason(fixture.reasonCode)}
        </p>
      ))}
    </div>
  )
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'Ocorreu um erro inesperado.'
}

function skipReason(reasonCode: WorldCupSkipReasonCode) {
  return reasonCode === 'missing_fifa_code'
    ? 'está faltando um código FIFA.'
    : 'um dos códigos FIFA não é suportado.'
}

function isInvalidField(field: keyof ManualForm, form: ManualForm, validation: string | null) {
  if (!validation) return false
  if (validation === 'Informe um ID de fixture inteiro e positivo.') {
    return field === 'providerFixtureId'
  }
  if (validation.includes('Europe/Berlin') || validation === 'Data e hora inválidas.') {
    return field === 'kickoff'
  }
  return !form[field].trim()
}
