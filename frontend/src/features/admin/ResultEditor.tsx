import { useEffect, useState } from 'react'

import type { ManualGoal, ManualResultDraft } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Field, FieldGroup, FieldLabel, FieldSet, FieldLegend } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { getRoster } from '@/data/rosters'
import { PlayerCombobox } from '@/features/players/PlayerCombobox'

type ResultEditorProps = {
  value: ManualResultDraft
  homeTeamFifaCode: string
  awayTeamFifaCode: string
  saving: boolean
  onSave: (value: ManualResultDraft) => Promise<void>
  onDirtyChange?: (dirty: boolean) => void
}

let nextGoalRowId = 0

function createGoalRowIds(goals: ManualGoal[]) {
  return goals.map(() => `goal-row-${nextGoalRowId++}`)
}

export function ResultEditor({
  value,
  homeTeamFifaCode,
  awayTeamFifaCode,
  saving,
  onSave,
  onDirtyChange,
}: ResultEditorProps) {
  const [draft, setDraft] = useState(value)
  const [goalRowIds, setGoalRowIds] = useState(() => createGoalRowIds(value.goals))
  const [validationErrors, setValidationErrors] = useState<string[]>([])

  useEffect(() => {
    setDraft(value)
    setGoalRowIds(createGoalRowIds(value.goals))
    setValidationErrors([])
  }, [value])

  const homeGoals = draft.goals.filter((goal) => goal.teamFifaCode === homeTeamFifaCode).length
  const awayGoals = draft.goals.filter((goal) => goal.teamFifaCode === awayTeamFifaCode).length
  const isDraw = homeGoals === awayGoals

  function markDirty() {
    setValidationErrors([])
    onDirtyChange?.(true)
  }

  function updateGoals(goals: ManualResultDraft['goals']) {
    markDirty()
    setDraft((current) => ({
      ...current,
      goals,
      penaltyWinnerTeamFifaCode: homeGoalsAfter(goals) === awayGoalsAfter(goals)
        ? current.penaltyWinnerTeamFifaCode
        : null,
    }))
  }

  function homeGoalsAfter(goals: ManualResultDraft['goals']) {
    return goals.filter((goal) => goal.teamFifaCode === homeTeamFifaCode).length
  }

  function awayGoalsAfter(goals: ManualResultDraft['goals']) {
    return goals.filter((goal) => goal.teamFifaCode === awayTeamFifaCode).length
  }

  function moveGoal(index: number, direction: -1 | 1) {
    const goals = [...draft.goals]
    const rowIds = [...goalRowIds]
    const target = index + direction
    const movedGoal = goals[index]
    goals[index] = goals[target]
    goals[target] = movedGoal
    const movedRowId = rowIds[index]
    rowIds[index] = rowIds[target]
    rowIds[target] = movedRowId
    setGoalRowIds(rowIds)
    updateGoals(goals)
  }

  function validate() {
    const errors: string[] = []
    if (draft.goals.some((goal) => !goal.teamFifaCode || !goal.playerKey)) {
      errors.push('Preencha o time e o jogador de todos os gols.')
    }
    const cards = [draft.homeYellowCards, draft.awayYellowCards, draft.homeRedCards, draft.awayRedCards]
    if (cards.some((cards) => !Number.isInteger(cards) || cards < 0)) {
      errors.push('Os cartões devem ser números inteiros não negativos.')
    }
    return errors
  }

  function save() {
    const errors = validate()
    setValidationErrors(errors)
    if (errors.length > 0) return
    void onSave(draft).catch(() => undefined)
  }

  const cardField = (
    key: keyof Pick<ManualResultDraft,
      'homeYellowCards' | 'awayYellowCards' | 'homeRedCards' | 'awayRedCards'>,
    label: string,
  ) => (
    <Field>
      <FieldLabel htmlFor={key}>{label}</FieldLabel>
      <Input
        id={key}
        min={0}
        type="number"
        value={draft[key]}
        onChange={(event) => {
          markDirty()
          setDraft((current) => ({
            ...current,
            [key]: Number(event.target.value),
          }))
        }}
      />
    </Field>
  )

  return (
    <FieldGroup>
      <FieldSet>
        <FieldLegend>Gols</FieldLegend>
        <p className="text-sm font-medium">Placar: {homeGoals} × {awayGoals}</p>
        <FieldGroup>
          {draft.goals.map((goal, index) => (
            <div key={goalRowIds[index]} className="grid gap-3 rounded-md border p-3 sm:grid-cols-[auto_1fr_2fr_auto] sm:items-end">
              <span className="pb-2 text-sm font-medium">{index + 1}.</span>
              <Field>
                <FieldLabel htmlFor={`goal-team-${index}`}>Time do gol {index + 1}</FieldLabel>
                <select
                  id={`goal-team-${index}`}
                  className="h-9 rounded-md border bg-background px-3 text-sm"
                  value={goal.teamFifaCode}
                  onChange={(event) => {
                    const goals = [...draft.goals]
                    goals[index] = { teamFifaCode: event.target.value, playerKey: '' }
                    updateGoals(goals)
                  }}
                >
                  <option value="">Selecione</option>
                  <option value={homeTeamFifaCode}>{homeTeamFifaCode}</option>
                  <option value={awayTeamFifaCode}>{awayTeamFifaCode}</option>
                </select>
              </Field>
              <PlayerCombobox
                label={`Jogador do gol ${index + 1}`}
                players={goal.teamFifaCode ? getRoster(goal.teamFifaCode).players : []}
                value={goal.playerKey || null}
                disabled={!goal.teamFifaCode}
                onChange={(playerKey) => {
                  const goals = [...draft.goals]
                  goals[index] = { ...goal, playerKey: playerKey ?? '' }
                  updateGoals(goals)
                }}
              />
              <div className="flex gap-1">
                <Button type="button" variant="outline" size="icon" aria-label={`Mover gol ${index + 1} para cima`} disabled={index === 0} onClick={() => moveGoal(index, -1)}>↑</Button>
                <Button type="button" variant="outline" size="icon" aria-label={`Mover gol ${index + 1} para baixo`} disabled={index === draft.goals.length - 1} onClick={() => moveGoal(index, 1)}>↓</Button>
                <Button type="button" variant="outline" size="icon" aria-label={`Remover gol ${index + 1}`} onClick={() => {
                  setGoalRowIds((current) => current.filter((_, goalIndex) => goalIndex !== index))
                  updateGoals(draft.goals.filter((_, goalIndex) => goalIndex !== index))
                }}>×</Button>
              </div>
            </div>
          ))}
        </FieldGroup>
        <Button type="button" variant="outline" onClick={() => {
          setGoalRowIds((current) => [...current, `goal-row-${nextGoalRowId++}`])
          updateGoals([...draft.goals, { teamFifaCode: '', playerKey: '' }])
        }}>
          Adicionar gol
        </Button>
      </FieldSet>

      <FieldSet>
        <FieldLegend>Cartões</FieldLegend>
        <FieldGroup className="grid grid-cols-2 gap-3">
          {cardField('homeYellowCards', 'Amarelos mandante')}
          {cardField('awayYellowCards', 'Amarelos visitante')}
          {cardField('homeRedCards', 'Vermelhos mandante')}
          {cardField('awayRedCards', 'Vermelhos visitante')}
        </FieldGroup>
      </FieldSet>

      <FieldSet>
        <FieldLegend>Pênaltis</FieldLegend>
        <p className="text-sm text-muted-foreground">Para escohler ganhador nos penaltis, o placar tem que ser um empate</p>
        <Field>
          <FieldLabel htmlFor="penalty-winner">Ganhador nos pênaltis</FieldLabel>
          <select
            id="penalty-winner"
            className="h-9 rounded-md border bg-background px-3 text-sm disabled:opacity-50"
            disabled={!isDraw}
            value={draft.penaltyWinnerTeamFifaCode ?? ''}
            onChange={(event) => {
              markDirty()
              setDraft((current) => ({
                ...current,
                penaltyWinnerTeamFifaCode: event.target.value || null,
              }))
            }}
          >
            <option value="">Selecione</option>
            <option value={homeTeamFifaCode}>{homeTeamFifaCode}</option>
            <option value={awayTeamFifaCode}>{awayTeamFifaCode}</option>
          </select>
        </Field>
      </FieldSet>

      {validationErrors.length > 0 ? (
        <div role="alert" className="text-sm text-destructive">
          {validationErrors.map((error) => <p key={error}>{error}</p>)}
        </div>
      ) : null}

      <Button type="button" variant="outline" disabled={saving} onClick={save}>
        Salvar resultado
      </Button>
    </FieldGroup>
  )
}
