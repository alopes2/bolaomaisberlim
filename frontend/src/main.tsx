import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'

import { ApiClient } from '@/api/client'
import { AuthProvider } from '@/auth/AuthProvider'
import { cognitoAuth, configureCognitoAuth } from '@/auth/cognito'

import './index.css'
import { App } from './App.tsx'

configureCognitoAuth()

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL
if (!apiBaseUrl) throw new Error('Configuração da API ausente.')

const api = new ApiClient(apiBaseUrl, cognitoAuth)
const queryClient = new QueryClient()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider client={cognitoAuth}>
      <QueryClientProvider client={queryClient}>
        <App api={api} />
      </QueryClientProvider>
    </AuthProvider>
  </StrictMode>,
)
