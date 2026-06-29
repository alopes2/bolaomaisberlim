import { useEffect, useState } from 'react'

export function useCutoff(cutoffAt: string) {
  const cutoff = new Date(cutoffAt).getTime()
  const [closed, setClosed] = useState(() => Date.now() >= cutoff)

  useEffect(() => {
    if (closed) return

    const interval = window.setInterval(() => {
      if (Date.now() >= cutoff) setClosed(true)
    }, 1_000)

    return () => window.clearInterval(interval)
  }, [closed, cutoff])

  return closed
}
