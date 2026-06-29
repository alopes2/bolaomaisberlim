import { CrownIcon } from 'lucide-react'

import type { RoundWinnerResponse } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'

export function RoundWinner({ winner }: { winner: RoundWinnerResponse | null }) {
  if (!winner) return null

  return (
    <Card>
      <CardHeader>
        <CardTitle>Vencedor da rodada</CardTitle>
        <CardDescription>Resultado confirmado pela organização.</CardDescription>
      </CardHeader>
      <CardContent className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Badge aria-label="Vencedor da rodada">
            <CrownIcon data-icon="inline-start" />
            1º
          </Badge>
          <strong>{winner.publicName}</strong>
        </div>
        <span>{winner.points} pontos</span>
      </CardContent>
    </Card>
  )
}
