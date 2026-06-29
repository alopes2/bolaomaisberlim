import { useEffect, useState, type ReactNode } from 'react'

import { AuthContext, type AuthStatus } from './auth-context'
import type { AuthClient } from './cognito'

export function AuthProvider({
  client,
  children,
}: {
  client: AuthClient
  children: ReactNode
}) {
  const [status, setStatus] = useState<AuthStatus>('checking')

  useEffect(() => {
    let active = true

    client
      .accessToken()
      .then((token) => {
        if (active) setStatus(token ? 'authenticated' : 'unauthenticated')
      })
      .catch(() => {
        if (active) setStatus('unauthenticated')
      })

    return () => {
      active = false
    }
  }, [client])

  async function refresh() {
    const token = await client.accessToken()
    setStatus(token ? 'authenticated' : 'unauthenticated')
  }

  async function leave() {
    await client.signOut()
    setStatus('unauthenticated')
  }

  return (
    <AuthContext.Provider value={{ client, status, refresh, signOut: leave }}>
      {children}
    </AuthContext.Provider>
  )
}
