import { Amplify } from 'aws-amplify'
import {
  confirmSignIn,
  fetchAuthSession,
  signIn,
  signOut,
} from 'aws-amplify/auth'

export interface AuthClient {
  start(email: string): Promise<void>
  confirm(code: string): Promise<void>
  signOut(): Promise<void>
  accessToken(): Promise<string | null>
}

export function configureCognitoAuth() {
  const userPoolId = import.meta.env.VITE_COGNITO_USER_POOL_ID
  const userPoolClientId = import.meta.env.VITE_COGNITO_CLIENT_ID

  if (!userPoolId || !userPoolClientId) {
    throw new Error('Configuração do Cognito ausente.')
  }

  Amplify.configure({
    Auth: {
      Cognito: {
        userPoolId,
        userPoolClientId,
        loginWith: { email: true },
      },
    },
  })
}

class CognitoAuthClient implements AuthClient {
  async start(email: string) {
    const result = await signIn({
      username: email,
      options: {
        authFlowType: 'USER_AUTH',
        preferredChallenge: 'EMAIL_OTP',
      },
    })

    if (result.nextStep.signInStep !== 'CONFIRM_SIGN_IN_WITH_EMAIL_CODE') {
      throw new Error('O Cognito não iniciou o desafio por e-mail.')
    }
  }

  async confirm(code: string) {
    const result = await confirmSignIn({ challengeResponse: code })
    if (result.nextStep.signInStep !== 'DONE') {
      throw new Error('O Cognito não concluiu a autenticação.')
    }
  }

  signOut() {
    return signOut()
  }

  async accessToken() {
    try {
      const session = await fetchAuthSession()
      return session.tokens?.accessToken?.toString() ?? null
    } catch (error) {
      if (error instanceof Error && error.name === 'UserUnAuthenticatedException') {
        return null
      }
      throw error
    }
  }
}

export const cognitoAuth = new CognitoAuthClient()
