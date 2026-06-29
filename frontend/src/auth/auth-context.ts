import { createContext, useContext } from 'react'

import type { AuthClient } from './cognito'

export type AuthStatus = 'checking' | 'authenticated' | 'unauthenticated'

export type AuthContextValue = {
  client: AuthClient
  status: AuthStatus
  refresh: () => Promise<void>
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth deve ser usado dentro de AuthProvider.')
  return context
}
