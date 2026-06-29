import { useState, type FormEvent } from 'react'

import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
} from '@/components/ui/input-otp'

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
  const [email, setEmail] = useState('')
  const [code, setCode] = useState('')
  const [codeRequested, setCodeRequested] = useState(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function requestCode(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setPending(true)
    setError(null)

    try {
      await auth.start(email)
      setCodeRequested(true)
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setPending(false)
    }
  }

  async function confirmCode(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setPending(true)
    setError(null)

    try {
      await auth.confirm(code)
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
            {codeRequested
              ? `Digite o código enviado para ${email}.`
              : 'Entre com seu e-mail para receber um código de acesso.'}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {codeRequested ? (
            <form onSubmit={confirmCode}>
              <FieldGroup>
                <Field data-invalid={Boolean(error)}>
                  <FieldLabel htmlFor="code">Código</FieldLabel>
                  <InputOTP
                    id="code"
                    aria-invalid={Boolean(error)}
                    autoComplete="one-time-code"
                    maxLength={6}
                    value={code}
                    onChange={setCode}
                    required
                  >
                    <InputOTPGroup>
                      {[0, 1, 2, 3, 4, 5].map((index) => (
                        <InputOTPSlot key={index} index={index} />
                      ))}
                    </InputOTPGroup>
                  </InputOTP>
                  <FieldError>{error}</FieldError>
                </Field>
                <Button type="submit" disabled={pending || code.length !== 6}>
                  {pending ? 'Confirmando…' : 'Confirmar código'}
                </Button>
              </FieldGroup>
            </form>
          ) : (
            <form onSubmit={requestCode}>
              <FieldGroup>
                <Field data-invalid={Boolean(error)}>
                  <FieldLabel htmlFor="email">E-mail</FieldLabel>
                  <Input
                    id="email"
                    type="email"
                    autoComplete="email"
                    aria-invalid={Boolean(error)}
                    value={email}
                    onChange={(event) => setEmail(event.target.value)}
                    required
                  />
                  <FieldError>{error}</FieldError>
                </Field>
                <Button type="submit" disabled={pending}>
                  {pending ? 'Enviando…' : 'Enviar código'}
                </Button>
              </FieldGroup>
            </form>
          )}
        </CardContent>
      </Card>
    </main>
  )
}
