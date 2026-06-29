import { useEffect, useState } from 'react'

import type { AdminResultResponse, ConfirmedResultResponse } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Field, FieldGroup, FieldLabel, FieldSet, FieldLegend } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { getRoster } from '@/data/rosters'
import { PlayerCombobox } from '@/features/players/PlayerCombobox'

type ResultEditorProps = {
  value: AdminResultResponse
  saving: boolean
  onSave: (value: AdminResultResponse) => Promise<void>
}

export function ResultEditor({ value, saving, onSave }: ResultEditorProps) {
  const [result, setResult] = useState(value.result)
  const [unresolvedPlayers, setUnresolvedPlayers] = useState(value.unresolvedPlayers)

  useEffect(() => {
    setResult(value.result)
    setUnresolvedPlayers(value.unresolvedPlayers)
  }, [value])

  const numberField = (
    key: keyof Pick<
      ConfirmedResultResponse,
      | 'homeGoals'
      | 'awayGoals'
      | 'homeYellowCards'
      | 'awayYellowCards'
      | 'homeRedCards'
      | 'awayRedCards'
    >,
    label: string,
  ) => (
    <Field>
      <FieldLabel htmlFor={key}>{label}</FieldLabel>
      <Input
        id={key}
        min={0}
        type="number"
        value={result[key]}
        onChange={(event) =>
          setResult((current) => ({ ...current, [key]: Number(event.target.value) }))
        }
      />
    </Field>
  )

  return (
    <FieldGroup>
      <FieldSet>
        <FieldLegend>Valores oficiais</FieldLegend>
        <FieldGroup className="grid grid-cols-2 gap-3">
          {numberField('homeGoals', 'Gols mandante')}
          {numberField('awayGoals', 'Gols visitante')}
          {numberField('homeYellowCards', 'Amarelos mandante')}
          {numberField('awayYellowCards', 'Amarelos visitante')}
          {numberField('homeRedCards', 'Vermelhos mandante')}
          {numberField('awayRedCards', 'Vermelhos visitante')}
        </FieldGroup>
      </FieldSet>

      <Field>
        <FieldLabel htmlFor="first-scorer">Chave do primeiro gol</FieldLabel>
        <Input
          id="first-scorer"
          value={result.firstScorerKey ?? ''}
          onChange={(event) =>
            setResult((current) => ({
              ...current,
              firstScorerKey: event.target.value || null,
            }))
          }
        />
      </Field>

      {unresolvedPlayers.length > 0 ? (
        <section aria-label="Jogadores não resolvidos" className="flex flex-col gap-2">
          <h3 className="text-sm font-medium">Mapeamentos pendentes</h3>
          {unresolvedPlayers.map((player) => (
            <div key={player.providerPlayerId} className="flex flex-col gap-2">
              <div className="flex items-center justify-between gap-2">
                <span>{player.providerName}</span>
                <Badge variant="secondary">{player.teamFifaCode}</Badge>
              </div>
              <PlayerCombobox
                label={`Associar ${player.providerName}`}
                players={getRoster(player.teamFifaCode).players}
                value={null}
                onChange={(playerKey) => {
                  if (!playerKey) return
                  setResult((current) => ({
                    ...current,
                    firstScorerKey: current.firstScorerKey ?? playerKey,
                  }))
                  setUnresolvedPlayers((current) =>
                    current.filter((item) => item.providerPlayerId !== player.providerPlayerId),
                  )
                }}
              />
            </div>
          ))}
        </section>
      ) : null}

      <Button
        type="button"
        variant="outline"
        disabled={saving}
        onClick={() => onSave({ ...value, result, unresolvedPlayers })}
      >
        Salvar correções
      </Button>
    </FieldGroup>
  )
}
