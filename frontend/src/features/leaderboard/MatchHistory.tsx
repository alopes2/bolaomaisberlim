import type { MatchResponse } from '@/api/client'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { getRoster } from '@/data/rosters'

export function MatchHistory({ matches }: { matches: MatchResponse[] }) {
  const orderedMatches = matches.toSorted(
    (left, right) => Date.parse(right.kickoff) - Date.parse(left.kickoff),
  )

  return (
    <Card>
      <CardHeader>
        <CardTitle>Jogos anteriores</CardTitle>
        <CardDescription>Histórico dos bolões do Brasil.</CardDescription>
      </CardHeader>
      <CardContent>
        {orderedMatches.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nenhum jogo anterior.</p>
        ) : (
          <ol className="flex flex-col gap-2">
            {orderedMatches.map((match) => {
              const home = getRoster(match.homeTeamFifaCode)
              const away = getRoster(match.awayTeamFifaCode)

              return (
                <li key={match.id} className="flex items-center justify-between gap-3 py-2">
                  <span>
                    {home.flagIcon} {home.name} × {away.name} {away.flagIcon}
                  </span>
                  <time dateTime={match.kickoff} className="text-sm text-muted-foreground">
                    {new Date(match.kickoff).toLocaleDateString('pt-BR')}
                  </time>
                </li>
              )
            })}
          </ol>
        )}
      </CardContent>
    </Card>
  )
}
