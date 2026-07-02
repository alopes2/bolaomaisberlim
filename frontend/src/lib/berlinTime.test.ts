import { describe, expect, it } from 'vitest'

import { berlinLocalToIso } from './berlinTime'

describe('berlinLocalToIso', () => {
  it('converts Berlin summer time independently from the device timezone', () => {
    expect(berlinLocalToIso('2026-06-15T18:00')).toBe('2026-06-15T16:00:00.000Z')
  })

  it('rejects a nonexistent Berlin time during the DST transition', () => {
    expect(() => berlinLocalToIso('2026-03-29T02:30')).toThrow(
      'Horário inexistente ou ambíguo em Europe/Berlin.',
    )
  })
})
