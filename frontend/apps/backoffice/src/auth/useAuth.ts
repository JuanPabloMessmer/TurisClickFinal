import { useAuthState } from '@turisclick/auth-core'
import { authManager } from './authManager'

export function useAuth() {
  const state = useAuthState(authManager)
  return {
    ...state,
    login: authManager.login.bind(authManager),
    logout: authManager.logout.bind(authManager),
  }
}
