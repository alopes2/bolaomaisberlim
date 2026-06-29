import { useState, type FormEvent } from 'react'

import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Field, FieldError, FieldGroup } from '@/components/ui/field'

import type { AuthClient } from './cognito'

type SignInPageProps = {
  auth: AuthClient
  onAuthenticated: () => void
}

function errorMessage(error: unknown) {
  return error instanceof Error
    ? error.message
    : 'Não foi possível concluir a autenticação.'
}

export function SignInPage({ auth, onAuthenticated }: SignInPageProps) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function signIn(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setPending(true)
    setError(null)

    try {
      await auth.signIn()
      onAuthenticated()
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setPending(false)
    }
  }

  return (
    <main className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle>Bolão MaisBerlim</CardTitle>
          <CardDescription>
            Entre com sua conta Google para continuar.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={signIn}>
            <FieldGroup>
              {error ? (
                <Field data-invalid>
                  <FieldError>{error}</FieldError>
                </Field>
              ) : null}
              <Button type="submit" disabled={pending}>
                {pending ? 'Entrando…' : 'Entrar com Google'}
              </Button>
            </FieldGroup>
          </form>
        </CardContent>
      </Card>
    </main>
  )
}
