import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'

import type { ApiClient } from '@/api/client'
import { useAuth } from '@/auth/auth-context'
import { ProfilePage } from '@/auth/ProfilePage'
import { SignInPage } from '@/auth/SignInPage'
import { Button } from '@/components/ui/button'
import { CurrentMatchPage } from '@/features/match/CurrentMatchPage'
import { PrivacyPage } from '@/features/legal/PrivacyPage'
import { RulesPage } from '@/features/legal/RulesPage'
import { AdminMatchPage } from '@/features/admin/AdminMatchPage'

export function App({ api }: { api: ApiClient }) {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [profileCompleted, setProfileCompleted] = useState(false)
  const [signingOut, setSigningOut] = useState(false)
  const profileQuery = useQuery({
    queryKey: ['profile-status'],
    queryFn: () => api.hasProfile(),
    enabled: auth.status === 'authenticated' && !window.location.pathname.startsWith('/admin'),
  })

  if (window.location.pathname === '/regras') return <RulesPage />
  if (window.location.pathname === '/privacidade') return <PrivacyPage />

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

  async function handleSignOut() {
    setSigningOut(true)
    try {
      await auth.signOut()
    } finally {
      setSigningOut(false)
    }
  }

  let page
  if (window.location.pathname.startsWith('/admin')) {
    const matchId = new URLSearchParams(window.location.search).get('matchId')
    page = matchId
      ? <AdminMatchPage api={api} matchId={matchId} />
      : <main className="p-4 text-sm text-muted-foreground">Informe o jogo com ?matchId=...</main>
  } else if (profileQuery.isPending && !profileCompleted) {
    page = <main className="p-4 text-sm text-muted-foreground">Verificando perfil…</main>
  } else {
    page = profileCompleted || profileQuery.data ? (
      <CurrentMatchPage api={api} />
    ) : (
      <ProfilePage api={api} onCompleted={() => {
        setProfileCompleted(true)
        queryClient.setQueryData(['profile-status'], true)
      }} />
    )
  }

  return (
    <>
      <header className="sticky top-0 z-40 border-b bg-background/95 backdrop-blur">
        <div className="mx-auto flex h-14 w-full max-w-3xl items-center justify-between px-4 sm:px-8">
          <span className="font-semibold">Bolão MaisBerlim</span>
          <Button variant="outline" onClick={handleSignOut} disabled={signingOut}>
            Sair
          </Button>
        </div>
      </header>
      {page}
    </>
  )
}
