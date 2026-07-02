const berlinFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Europe/Berlin',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
})

export function berlinLocalToIso(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value)
  if (!match) throw new Error('Data e hora inválidas.')

  const local = match.slice(1).map(Number)
  const [year, month, day, hour, minute] = local
  const localAsUtc = Date.UTC(year, month - 1, day, hour, minute)
  const normalized = new Date(localAsUtc)
  if (normalized.getUTCFullYear() !== year
    || normalized.getUTCMonth() !== month - 1
    || normalized.getUTCDate() !== day
    || normalized.getUTCHours() !== hour
    || normalized.getUTCMinutes() !== minute) {
    throw new Error('Data e hora inválidas.')
  }

  const candidates: number[] = []
  for (let offsetMinutes = -14 * 60; offsetMinutes <= 14 * 60; offsetMinutes += 15) {
    const instant = localAsUtc - offsetMinutes * 60_000
    if (isSameBerlinLocalTime(instant, local)) candidates.push(instant)
  }
  if (candidates.length !== 1) {
    throw new Error('Horário inexistente ou ambíguo em Europe/Berlin.')
  }
  return new Date(candidates[0]).toISOString()
}

export function isoToBerlinLocal(value: string) {
  const parts = Object.fromEntries(
    berlinFormatter.formatToParts(new Date(value))
      .filter(part => part.type !== 'literal')
      .map(part => [part.type, part.value]),
  )
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`
}

function isSameBerlinLocalTime(instant: number, expected: number[]) {
  const parts = Object.fromEntries(
    berlinFormatter.formatToParts(instant)
      .filter(part => part.type !== 'literal')
      .map(part => [part.type, Number(part.value)]),
  )
  return parts.year === expected[0]
    && parts.month === expected[1]
    && parts.day === expected[2]
    && parts.hour === expected[3]
    && parts.minute === expected[4]
}
