import { AuthManager, type TokenStorage } from '@turisclick/auth-core'
import axios, { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'

/**
 * AuthManager REAL con un axios REAL: sólo se reemplaza el adapter de red. Así se ejercitan los
 * interceptores (Bearer, 401 → refresh → reintento), que los tests de SessionProvider no cubren porque
 * mockean authApi completo.
 */

type Handler = (config: InternalAxiosRequestConfig) => { status: number; data?: unknown } | 'network-error'

function createHttp(handler: Handler) {
  const calls: { url?: string; authorization?: string }[] = []
  const adapter: AxiosAdapter = async (config) => {
    calls.push({ url: config.url, authorization: config.headers?.get?.('Authorization') as string | undefined })
    const result = handler(config)
    if (result === 'network-error') {
      throw new AxiosError('Network Error', AxiosError.ERR_NETWORK, config)
    }
    const response: AxiosResponse = { status: result.status, statusText: '', headers: {}, config, data: result.data ?? {} }
    if (result.status >= 400) {
      throw new AxiosError(`Request failed with status code ${result.status}`, AxiosError.ERR_BAD_REQUEST, config, null, response)
    }
    return response
  }
  return { http: axios.create({ baseURL: 'https://api.test', adapter }), calls }
}

function memoryStorage(initial: string | null = null): TokenStorage & { value: string | null } {
  return {
    value: initial,
    async getRefreshToken() {
      return this.value
    },
    async setRefreshToken(token: string) {
      this.value = token
    },
    async clearRefreshToken() {
      this.value = null
    },
  }
}

const tourist = { id: 'u1', email: 'ana@example.com', fullName: 'Ana', role: 'TOURIST' }

/** Falla el test en vez de colgarlo: el bug que cubre este archivo era justamente un deadlock. */
function withinTimeout<T>(promise: Promise<T>, ms = 2000): Promise<T> {
  return Promise.race([
    promise,
    new Promise<T>((_, reject) => setTimeout(() => reject(new Error(`no terminó en ${ms} ms (deadlock)`)), ms)),
  ])
}

describe('AuthManager — refresh y sesión', () => {
  it('bootstrap con un refresh token inválido termina desautenticado y lo descarta, sin colgarse', async () => {
    const { http, calls } = createHttp((config) =>
      config.url === '/api/auth/refresh' ? { status: 401, data: { detail: 'Refresh token inválido' } } : { status: 200 },
    )
    const storage = memoryStorage('token-de-otro-backend')
    const manager = new AuthManager(http, storage)

    await withinTimeout(manager.bootstrap())

    expect(manager.store.getSnapshot().status).toBe('unauthenticated')
    expect(storage.value).toBeNull()
    expect(calls.filter((c) => c.url === '/api/auth/refresh')).toHaveLength(1)
  })

  it('bootstrap sin red queda desautenticado pero conserva el token para reintentar al volver la conexión', async () => {
    const { http } = createHttp(() => 'network-error')
    const storage = memoryStorage('token-valido')
    const manager = new AuthManager(http, storage)

    await withinTimeout(manager.bootstrap())

    expect(manager.store.getSnapshot().status).toBe('unauthenticated')
    expect(storage.value).toBe('token-valido')
  })

  it('bootstrap con refresh válido restaura la sesión y rota el token', async () => {
    const { http } = createHttp((config) =>
      config.url === '/api/auth/refresh'
        ? { status: 200, data: { accessToken: 'access-2', refreshToken: 'refresh-2', user: tourist } }
        : { status: 200 },
    )
    const storage = memoryStorage('refresh-1')
    const manager = new AuthManager(http, storage)

    await withinTimeout(manager.bootstrap())

    expect(manager.store.getSnapshot()).toMatchObject({ status: 'authenticated', accessToken: 'access-2' })
    expect(storage.value).toBe('refresh-2')
  })

  it('un 401 en un endpoint protegido renueva el token una vez y reintenta con el nuevo', async () => {
    let refreshed = false
    const { http, calls } = createHttp((config) => {
      if (config.url === '/api/auth/refresh') {
        refreshed = true
        return { status: 200, data: { accessToken: 'access-2', refreshToken: 'refresh-2', user: tourist } }
      }
      return refreshed ? { status: 200, data: { ok: true } } : { status: 401 }
    })
    const manager = new AuthManager(http, memoryStorage('refresh-1'))

    const response = await withinTimeout(http.get('/api/reservations/me'))

    expect(response.data).toEqual({ ok: true })
    const protectedCalls = calls.filter((c) => c.url === '/api/reservations/me')
    expect(protectedCalls).toHaveLength(2)
    expect(protectedCalls[1].authorization).toBe('Bearer access-2')
    expect(manager.store.getSnapshot().status).toBe('authenticated')
  })

  it('si el refresh también responde 401, el request protegido falla y la sesión se limpia, sin colgarse', async () => {
    const { http } = createHttp(() => ({ status: 401 }))
    const storage = memoryStorage('refresh-vencido')
    const manager = new AuthManager(http, storage)

    await expect(withinTimeout(http.get('/api/reservations/me'))).rejects.toMatchObject({ response: { status: 401 } })

    expect(manager.store.getSnapshot().status).toBe('unauthenticated')
    expect(storage.value).toBeNull()
  })

  it('un login con credenciales incorrectas no intenta renovar tokens', async () => {
    const { http, calls } = createHttp((config) =>
      config.url === '/api/auth/login' ? { status: 401, data: { detail: 'Email o contraseña inválidos.' } } : { status: 200 },
    )
    const manager = new AuthManager(http, memoryStorage('refresh-de-una-sesion-anterior'))

    await expect(withinTimeout(manager.login({ email: 'ana@example.com', password: 'mal' }))).rejects.toMatchObject({
      response: { status: 401 },
    })

    expect(calls.some((c) => c.url === '/api/auth/refresh')).toBe(false)
  })
})
