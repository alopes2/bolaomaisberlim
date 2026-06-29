import type { LeaderboardResponse } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

export function ProvisionalLeaderboard({ leaderboard }: { leaderboard: LeaderboardResponse }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Ranking provisório</CardTitle>
        <CardDescription>Visível apenas para administradores até a confirmação.</CardDescription>
      </CardHeader>
      <CardContent>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Posição</TableHead>
              <TableHead>Participante</TableHead>
              <TableHead className="text-right">Pontos</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {leaderboard.entries.map((entry) => (
              <TableRow key={entry.position}>
                <TableCell><Badge variant="secondary">{entry.position}º</Badge></TableCell>
                <TableCell>{entry.publicName}</TableCell>
                <TableCell className="text-right">{entry.totalPoints}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  )
}
