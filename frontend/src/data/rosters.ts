import teams from '../../../assets/teams.json'

type JsonTeam = {
  name: string
  flag_icon: string
  fifa_code: string
  players: Array<{ number: number; name: string }>
}

const displayNames = new Intl.DisplayNames(['pt-BR'], { type: 'region' })

function regionCode(flagIcon: string) {
  const letters = [...flagIcon].map((character) => {
    const codePoint = character.codePointAt(0)
    return codePoint && codePoint >= 0x1f1e6 && codePoint <= 0x1f1ff
      ? String.fromCharCode(codePoint - 0x1f1e6 + 65)
      : ''
  })

  return letters.length === 2 && letters.every(Boolean) ? letters.join('') : null
}

export function getRoster(fifaCode: string) {
  const team = (teams as JsonTeam[]).find(
    (candidate) => candidate.fifa_code === fifaCode,
  )
  if (!team) throw new Error(`Seleção ${fifaCode} não encontrada.`)
  const countryCode = regionCode(team.flag_icon)

  return {
    fifaCode: team.fifa_code,
    name: (countryCode && displayNames.of(countryCode)) || team.name,
    flagIcon: team.flag_icon,
    players: team.players.map((player) => ({
      key: `${team.fifa_code}:${player.number}`,
      name: player.name,
    })),
  }
}
