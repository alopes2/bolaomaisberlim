import { CrownIcon } from 'lucide-react'

import type { LeaderboardEntry } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'

export function Leaderboard({ entries }: { entries: LeaderboardEntry[] }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Ranking</CardTitle>
        <CardDescription>Classificação com resultados confirmados.</CardDescription>
      </CardHeader>
      <CardContent>
        {entries.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            O ranking será publicado após o primeiro resultado confirmado.
          </p>
        ) : (
          <ol className="flex flex-col gap-2">
            {entries.map((entry) => (
              <li key={entry.position}>
                <Card size={entry.position === 1 ? 'default' : 'sm'}>
                  <CardContent className="grid grid-cols-[auto_1fr_auto] items-center gap-3">
                    {entry.position === 1 ? (
                      <Badge aria-label="Primeiro lugar">
                        <CrownIcon data-icon="inline-start" />
                        1º
                      </Badge>
                    ) : (
                      <Badge variant="secondary">{entry.position}º</Badge>
                    )}
                    <span data-rank={entry.position} className="font-medium">
                      {entry.publicName}
                    </span>
                    <strong>{entry.totalPoints} pts</strong>
                  </CardContent>
                </Card>
              </li>
            ))}
          </ol>
        )}
      </CardContent>
    </Card>
  )
}
