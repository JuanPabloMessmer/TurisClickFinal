import { useSyncExternalStore } from 'react'
import type { AuthManager } from './authManager'

/** Hook compartible entre React DOM (Backoffice) y React Native (Tourist Mobile) — es solo lógica, sin JSX. */
export function useAuthState(manager: AuthManager) {
  return useSyncExternalStore(manager.store.subscribe, manager.store.getSnapshot)
}
