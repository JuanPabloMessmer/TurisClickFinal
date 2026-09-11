import { authApi, type RegisterTouristRequest, type UserSummaryResponse } from '@turisclick/api-client'
import { AuthManager, useAuthState } from '@turisclick/auth-core'
import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { httpClient } from '@/lib/httpClient'
import { secureTokenStorage } from '@/lib/secureTokenStorage'

/**
 * Se lanza cuando alguien con una cuenta que no es de turista intenta entrar. No es un error de red ni
 * del backend: la credencial es válida, pero esta app no es para ese rol.
 */
export class NotATouristAccountError extends Error {
  constructor() {
    super('Esta cuenta no es de turista. Usá el Backoffice de TurisClick para cuentas de proveedor o administrador.')
    this.name = 'NotATouristAccountError'
  }
}

const TOURIST_ROLE = 'TOURIST'

interface SessionContextValue {
  status: 'idle' | 'loading' | 'authenticated' | 'unauthenticated'
  user: UserSummaryResponse | null
  isAuthenticated: boolean
  login: (email: string, password: string) => Promise<void>
  register: (body: RegisterTouristRequest) => Promise<void>
  logout: () => Promise<void>
}

const SessionContext = createContext<SessionContextValue | null>(null)

export function SessionProvider({ children }: { children: ReactNode }) {
  // Una sola instancia por vida de la app: el AuthManager engancha interceptores en el httpClient y
  // volver a crearlo los duplicaría.
  const managerRef = useRef<AuthManager>(undefined)
  managerRef.current ??= new AuthManager(httpClient, secureTokenStorage)
  const manager = managerRef.current

  const state = useAuthState(manager)
  const [bootstrapped, setBootstrapped] = useState(false)

  useEffect(() => {
    let cancelled = false

    void (async () => {
      await manager.bootstrap()
      if (cancelled) return

      // Role gating también al restaurar sesión, no solo al hacer login: el refresh token guardado
      // podría ser de una cuenta que no corresponde a esta app (ej. se probó con un PROVIDER).
      const restored = manager.store.getSnapshot()
      if (restored.status === 'authenticated' && restored.user?.role !== TOURIST_ROLE) {
        await manager.logout()
      }
      setBootstrapped(true)
    })()

    return () => {
      cancelled = true
    }
  }, [manager])

  const value = useMemo<SessionContextValue>(
    () => ({
      // Mientras no terminó el bootstrap la sesión es indeterminada: 'idle' mantiene el splash y evita
      // que un guard mande a login a alguien que en realidad tenía sesión válida.
      status: bootstrapped ? state.status : 'idle',
      user: state.user,
      isAuthenticated: bootstrapped && state.status === 'authenticated',

      async login(email, password) {
        const result = await manager.login({ email, password })
        await ensureTourist(manager, result.user?.role)
      },

      async register(body) {
        // El registro devuelve la sesión ya iniciada; se hidrata igual que un login para no duplicar
        // la lógica de applySession.
        const result = await authApi.registerTourist(httpClient, body)
        manager.applyExternalSession(result)
        await ensureTourist(manager, result.user?.role)
      },

      logout: () => manager.logout(),
    }),
    [manager, state.status, state.user, bootstrapped],
  )

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}

/** Si el rol no es TOURIST se limpia la sesión y los tokens antes de dejar que la pantalla siga. */
async function ensureTourist(manager: AuthManager, role: string | null | undefined) {
  if (role === TOURIST_ROLE) return
  await manager.logout()
  throw new NotATouristAccountError()
}

export function useSession(): SessionContextValue {
  const context = useContext(SessionContext)
  if (!context) throw new Error('useSession debe usarse dentro de <SessionProvider>')
  return context
}
