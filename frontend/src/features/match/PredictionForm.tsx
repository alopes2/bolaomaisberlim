import { zodResolver } from '@hookform/resolvers/zod';
import { Controller, useForm } from 'react-hook-form';
import { z } from 'zod';

import { Button } from '@/components/ui/button';
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  PlayerCombobox,
  type PlayerOption,
} from '@/features/players/PlayerCombobox';

import { useCutoff } from './useCutoff';
import type { PredictionAnswers } from '@/api/client';

const predictionSchema = z.object({
  homeGoals: z.number().int().min(0),
  awayGoals: z.number().int().min(0),
  firstScorerKey: z.string().min(1),
  homeTopScorerKey: z.string().min(1),
  awayTopScorerKey: z.string().min(1),
  homeYellowCards: z.number().int().min(0),
  awayYellowCards: z.number().int().min(0),
  homeRedCards: z.number().int().min(0),
  awayRedCards: z.number().int().min(0),
});

export type PredictionValues = z.infer<typeof predictionSchema>;

type PredictionFormProps = {
  homeTeam: string;
  awayTeam: string;
  homePlayers: PlayerOption[];
  awayPlayers: PlayerOption[];
  cutoffAt: string;
  submittedAt?: string | null;
  storedPrediction?: PredictionAnswers | null;
  onSubmit: (prediction: PredictionValues) => void | Promise<void>;
};

export function PredictionForm({
  homeTeam,
  awayTeam,
  homePlayers,
  awayPlayers,
  cutoffAt,
  submittedAt = null,
  onSubmit,
  storedPrediction,
}: PredictionFormProps) {
  const closed = useCutoff(cutoffAt);
  let defaultValues: PredictionValues = {
    homeGoals: 0,
    awayGoals: 0,
    firstScorerKey: '',
    homeTopScorerKey: '',
    awayTopScorerKey: '',
    homeYellowCards: 0,
    awayYellowCards: 0,
    homeRedCards: 0,
    awayRedCards: 0,
  };
  if (storedPrediction) {
    defaultValues = {
      homeGoals: storedPrediction.homeGoals,
      awayGoals: storedPrediction.awayGoals,
      firstScorerKey: storedPrediction.firstScorerKey,
      homeTopScorerKey: storedPrediction.homeTopScorerKey,
      awayTopScorerKey: storedPrediction.awayTopScorerKey,
      homeYellowCards: storedPrediction.homeYellowCards,
      awayYellowCards: storedPrediction.awayYellowCards,
      homeRedCards: storedPrediction.homeRedCards,
      awayRedCards: storedPrediction.awayRedCards,
    };
  }
  const form = useForm<PredictionValues>({
    resolver: zodResolver(predictionSchema),
    defaultValues: defaultValues,
  });
  const firstScorers = [
    ...homePlayers.map((player) => ({ ...player, team: homeTeam })),
    ...awayPlayers.map((player) => ({ ...player, team: awayTeam })),
  ];

  return (
    <form onSubmit={form.handleSubmit(onSubmit)}>
      <FieldGroup>
        <FieldSet disabled={closed}>
          <FieldLegend>Placar</FieldLegend>
          <FieldGroup className="grid grid-cols-2 gap-3">
            <Field>
              <FieldLabel htmlFor="home-goals">{homeTeam}</FieldLabel>
              <Input
                id="home-goals"
                type="number"
                min={0}
                {...form.register('homeGoals', { valueAsNumber: true })}
              />
            </Field>
            <Field>
              <FieldLabel htmlFor="away-goals">{awayTeam}</FieldLabel>
              <Input
                id="away-goals"
                type="number"
                min={0}
                {...form.register('awayGoals', { valueAsNumber: true })}
              />
            </Field>
          </FieldGroup>
        </FieldSet>

        <Controller
          control={form.control}
          name="firstScorerKey"
          render={({ field }) => (
            <PlayerCombobox
              label="Primeiro gol"
              players={firstScorers}
              value={field.value || null}
              onChange={(value) => field.onChange(value ?? '')}
              disabled={closed}
            />
          )}
        />
        <Controller
          control={form.control}
          name="homeTopScorerKey"
          render={({ field }) => (
            <PlayerCombobox
              label={`Artilheiro ${homeTeam}`}
              players={homePlayers}
              value={field.value || null}
              onChange={(value) => field.onChange(value ?? '')}
              disabled={closed}
            />
          )}
        />
        <Controller
          control={form.control}
          name="awayTopScorerKey"
          render={({ field }) => (
            <PlayerCombobox
              label={`Artilheiro ${awayTeam}`}
              players={awayPlayers}
              value={field.value || null}
              onChange={(value) => field.onChange(value ?? '')}
              disabled={closed}
            />
          )}
        />

        <FieldSet disabled={closed}>
          <FieldLegend>Cartões</FieldLegend>
          <FieldGroup className="grid grid-cols-2 gap-3">
            <Field>
              <FieldLabel htmlFor="home-yellow">Amarelos {homeTeam}</FieldLabel>
              <Input
                id="home-yellow"
                type="number"
                min={0}
                {...form.register('homeYellowCards', { valueAsNumber: true })}
              />
            </Field>
            <Field>
              <FieldLabel htmlFor="away-yellow">Amarelos {awayTeam}</FieldLabel>
              <Input
                id="away-yellow"
                type="number"
                min={0}
                {...form.register('awayYellowCards', { valueAsNumber: true })}
              />
            </Field>
            <Field>
              <FieldLabel htmlFor="home-red">Vermelhos {homeTeam}</FieldLabel>
              <Input
                id="home-red"
                type="number"
                min={0}
                {...form.register('homeRedCards', { valueAsNumber: true })}
              />
            </Field>
            <Field>
              <FieldLabel htmlFor="away-red">Vermelhos {awayTeam}</FieldLabel>
              <Input
                id="away-red"
                type="number"
                min={0}
                {...form.register('awayRedCards', { valueAsNumber: true })}
              />
            </Field>
          </FieldGroup>
        </FieldSet>

        {submittedAt ? (
          <FieldDescription>
            Último envio: {new Date(submittedAt).toLocaleString('pt-BR')}
          </FieldDescription>
        ) : null}
        <Button type="submit" disabled={closed || form.formState.isSubmitting}>
          {closed ? 'Palpites encerrados' : 'Salvar palpite'}
        </Button>
      </FieldGroup>
    </form>
  );
}
