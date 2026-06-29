import { Amplify } from 'aws-amplify'
import {
  fetchAuthSession,
  signInWithRedirect,
  signOut,
} from 'aws-amplify/auth'

export interface AuthClient {
  signIn(): Promise<void>
  signOut(): Promise<void>
  accessToken(): Promise<string | null>
}

export function configureCognitoAuth() {
  if (import.meta.env.VITE_E2E === 'true') return
  const userPoolId = import.meta.env.VITE_COGNITO_USER_POOL_ID
  const userPoolClientId = import.meta.env.VITE_COGNITO_CLIENT_ID
  const domain = import.meta.env.VITE_COGNITO_DOMAIN

  if (!userPoolId || !userPoolClientId || !domain) {
    throw new Error('Configuração do Cognito ausente.')
  }

  const redirectUrl = `${window.location.origin}/`
  Amplify.configure({
    Auth: {
      Cognito: {
        userPoolId,
        userPoolClientId,
        loginWith: {
          oauth: {
            domain,
            scopes: ['openid', 'email', 'profile'],
            redirectSignIn: [redirectUrl],
            redirectSignOut: [redirectUrl],
            responseType: 'code',
          },
        },
      },
    },
  })
}

class E2EAuthClient implements AuthClient {
  async signIn() {
    localStorage.setItem('bolao-e2e-token', 'e2e-user')
  }

  async signOut() {
    localStorage.removeItem('bolao-e2e-token')
  }

  async accessToken() {
    return localStorage.getItem('bolao-e2e-token')
  }
}

export class CognitoAuthClient implements AuthClient {
  signIn() {
    return signInWithRedirect({ provider: 'Google' })
  }

  signOut() {
    return signOut()
  }

  async accessToken() {
    try {
      const session = await fetchAuthSession()
      return session.tokens?.accessToken?.toString() ?? null
    } catch (error) {
      if (
        error instanceof Error &&
        error.name === 'UserUnAuthenticatedException'
      ) {
        return null
      }
      throw error
    }
  }
}

export const cognitoAuth = import.meta.env.VITE_E2E === 'true'
  ? new E2EAuthClient()
  : new CognitoAuthClient()
