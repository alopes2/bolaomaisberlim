import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Leaderboard } from './Leaderboard'
import { MatchHistory } from './MatchHistory'
import { RoundWinner } from './RoundWinner'

const entries = [
  {
    position: 1,
    publicName: 'Ana S.',
    totalPoints: 18,
    exactScoreCount: 1,
    firstScorerCount: 1,
  },
  {
    position: 2,
    publicName: 'Bruno M.',
    totalPoints: 15,
    exactScoreCount: 0,
    firstScorerCount: 1,
  },
]

describe('Leaderboard', () => {
  it('highlights first place with an accessible crown label', () => {
    render(<Leaderboard entries={entries} />)

    expect(screen.getByText('Ana S.')).toHaveAttribute('data-rank', '1')
    expect(screen.getByLabelText(/primeiro lugar/i)).toBeVisible()
  })

  it('renders the confirmed round winner', () => {
    render(<RoundWinner winner={{ publicName: 'Ana S.', points: 18 }} />)

    expect(screen.getByText('Ana S.')).toBeVisible()
    expect(screen.getByText(/18 pontos/i)).toBeVisible()
  })

  it('orders match history by most recent kickoff', () => {
    render(
      <MatchHistory
        matches={[
          { id: 'old', kickoff: '2026-06-20T18:00:00Z', homeTeamFifaCode: 'BRA', awayTeamFifaCode: 'ARG' },
          { id: 'new', kickoff: '2026-06-25T18:00:00Z', homeTeamFifaCode: 'BRA', awayTeamFifaCode: 'GER' },
        ]}
      />,
    )

    const items = screen.getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('Alemanha')
    expect(items[1]).toHaveTextContent('Argentina')
  })
})
