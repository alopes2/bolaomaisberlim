import { useState, type FormEvent } from 'react'

import type { ProfileApi, ProfileResponse } from '@/api/client'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'

type ProfilePageProps = {
  api: ProfileApi
  onCompleted?: () => void
}

function publicLabel(profile: ProfileResponse) {
  return [profile.publicName, profile.suffix].filter(Boolean).join(' ')
}

export function ProfilePage({ api, onCompleted }: ProfilePageProps) {
  const [givenName, setGivenName] = useState('')
  const [familyName, setFamilyName] = useState('')
  const [profile, setProfile] = useState<ProfileResponse | null>(null)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setPending(true)
    setError(null)

    try {
      setProfile(await api.saveProfile(givenName, familyName))
    } catch (caught) {
      setError(
        caught instanceof Error
          ? caught.message
          : 'Não foi possível salvar seu perfil.',
      )
    } finally {
      setPending(false)
    }
  }

  if (profile) {
    return (
      <main className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
        <Card className="w-full max-w-md">
          <CardHeader>
            <CardTitle>Perfil salvo</CardTitle>
            <CardDescription>Seu nome no ranking será:</CardDescription>
          </CardHeader>
          <CardContent>
            <p className="text-lg font-medium">{publicLabel(profile)}</p>
          </CardContent>
          {onCompleted ? (
            <CardFooter>
              <Button type="button" onClick={onCompleted}>
                Continuar
              </Button>
            </CardFooter>
          ) : null}
        </Card>
      </main>
    )
  }

  return (
    <main className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle>Complete seu perfil</CardTitle>
          <CardDescription>
            Seu nome completo será privado. No ranking mostraremos apenas o
            primeiro nome e a inicial do sobrenome.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={save}>
            <FieldGroup>
              <Field>
                <FieldLabel htmlFor="given-name">Nome</FieldLabel>
                <Input
                  id="given-name"
                  autoComplete="given-name"
                  value={givenName}
                  onChange={(event) => setGivenName(event.target.value)}
                  required
                />
              </Field>
              <Field>
                <FieldLabel htmlFor="family-name">Sobrenome</FieldLabel>
                <Input
                  id="family-name"
                  autoComplete="family-name"
                  value={familyName}
                  onChange={(event) => setFamilyName(event.target.value)}
                  required
                />
              </Field>
              {error ? (
                <Field data-invalid>
                  <FieldError>{error}</FieldError>
                </Field>
              ) : null}
              <Button type="submit" disabled={pending}>
                {pending ? 'Salvando…' : 'Salvar perfil'}
              </Button>
            </FieldGroup>
          </form>
        </CardContent>
      </Card>
    </main>
  )
}
