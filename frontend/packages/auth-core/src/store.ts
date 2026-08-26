import type { UserSummaryResponse } from '@turisclick/api-client'

export interface AuthState {
  /** idle: todavía no se intentó restaurar sesión. loading: login o bootstrap en curso. */
  status: 'idle' | 'loading' | 'authenticated' | 'unauthenticated'
  accessToken: string | null
  user: UserSummaryResponse | null
}

type Listener = () => void

/** Store reactivo mínimo, sin dependencia de React — compatible con useSyncExternalStore en web y RN. */
export class AuthStore {
  private state: AuthState = { status: 'idle', accessToken: null, user: null }
  private listeners = new Set<Listener>()

  getSnapshot = (): AuthState => this.state

  subscribe = (listener: Listener): (() => void) => {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  setState(patch: Partial<AuthState>) {
    this.state = { ...this.state, ...patch }
    for (const listener of this.listeners) listener()
  }
}
