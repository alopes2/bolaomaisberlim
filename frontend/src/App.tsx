import { useState } from 'react'

import type { ApiClient } from '@/api/client'
import { useAuth } from '@/auth/auth-context'
import { ProfilePage } from '@/auth/ProfilePage'
import { SignInPage } from '@/auth/SignInPage'
import { CurrentMatchPage } from '@/features/match/CurrentMatchPage'

export function App({ api }: { api: ApiClient }) {
  const auth = useAuth()
  const [profileCompleted, setProfileCompleted] = useState(false)

  if (auth.status === 'checking') {
    return (
      <main className="flex min-h-svh items-center justify-center p-4">
        <p className="text-sm text-muted-foreground">Verificando sessão…</p>
      </main>
    )
  }

  if (auth.status === 'unauthenticated') {
    return <SignInPage auth={auth.client} onAuthenticated={auth.refresh} />
  }

  return profileCompleted ? (
    <CurrentMatchPage api={api} />
  ) : (
    <ProfilePage api={api} onCompleted={() => setProfileCompleted(true)} />
  )
}
