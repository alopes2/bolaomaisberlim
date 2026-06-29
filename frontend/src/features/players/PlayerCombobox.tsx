import { useId } from 'react';

import {
  Combobox,
  ComboboxContent,
  ComboboxCollection,
  ComboboxEmpty,
  ComboboxGroup,
  ComboboxInput,
  ComboboxItem,
  ComboboxLabel,
  ComboboxList,
} from '@/components/ui/combobox';
import { Field, FieldLabel } from '@/components/ui/field';

export type PlayerOption = {
  key: string;
  name: string;
  team?: string;
};

type PlayerComboboxProps = {
  label: string;
  players: PlayerOption[];
  value: string | null;
  onChange: (value: string | null) => void;
  disabled?: boolean;
};

const normalize = (value: string) =>
  value
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLocaleLowerCase('pt-BR');

export function PlayerCombobox({
  label,
  players,
  value,
  onChange,
  disabled = false,
}: PlayerComboboxProps) {
  const inputId = useId();
  const selected = players.find((player) => player.key === value)?.name ?? null;
  const teams = [
    ...new Set(players.map((player) => player.team).filter(Boolean)),
  ];
  const groups = teams.map((team) => ({
    team,
    items: players.filter((player) => player.team === team),
  }));

  const renderPlayer = (player: PlayerOption) => (
    <ComboboxItem key={player.key} value={player}>
      {player.name}
    </ComboboxItem>
  );

  return (
    <Field data-disabled={disabled}>
      <FieldLabel htmlFor={inputId}>{label}</FieldLabel>
      <Combobox
        items={teams.length > 1 ? groups : players}
        value={selected}
        onValueChange={(player) => onChange(player?.key ?? null)}
        itemToStringValue={(player) => player.name}
        filter={(player, query) =>
          normalize(player.name).includes(normalize(query))
        }
        autoHighlight
        disabled={disabled}
      >
        <ComboboxInput id={inputId} disabled={disabled} />
        <ComboboxContent>
          <ComboboxEmpty>Nenhum jogador encontrado.</ComboboxEmpty>
          <ComboboxList>
            {teams.length > 1
              ? groups.map((group) => (
                  <ComboboxGroup key={group.team} items={group.items}>
                    <ComboboxLabel>{group.team}</ComboboxLabel>
                    <ComboboxCollection>{renderPlayer}</ComboboxCollection>
                  </ComboboxGroup>
                ))
              : renderPlayer}
          </ComboboxList>
        </ComboboxContent>
      </Combobox>
    </Field>
  );
}
