import { authApi, type AuthResultResponse, type LoginRequest } from '@turisclick/api-client'
import { isAxiosError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { AuthStore } from './store'
import type { TokenStorage } from './tokenStorage'

interface RetryableConfig extends InternalAxiosRequestConfig {
  _retry?: boolean
}

/**
 * Un 401 en estos endpoints es la respuesta final (credenciales o refresh token inválidos), nunca un
 * access token vencido. Intentar renovar acá además colgaría la app: el 401 del propio refresh
 * esperaría a la promesa de refresh que está esperando esa misma respuesta.
 */
const AUTH_ENDPOINTS = ['/api/auth/login', '/api/auth/refresh', '/api/auth/register', '/api/auth/logout']

function isAuthEndpoint(url: string | undefined): boolean {
  return !!url && AUTH_ENDPOINTS.some((endpoint) => url.includes(endpoint))
}

/**
 * Orquesta login/logout/refresh y engancha los interceptores de Axios (adjuntar Bearer, reintentar una
 * vez tras un 401 renovando el access token). Agnóstico de plataforma: recibe el AxiosInstance y el
 * TokenStorage ya armados por la app — no sabe si corre en el navegador o en Expo.
 */
export class AuthManager {
  readonly store = new AuthStore()
  private readonly http: AxiosInstance
  private readonly storage: TokenStorage
  private refreshPromise: Promise<string | null> | null = null

  constructor(http: AxiosInstance, storage: TokenStorage) {
    this.http = http
    this.storage = storage
    this.attachInterceptors()
  }

  private attachInterceptors() {
    this.http.interceptors.request.use((config) => {
      const token = this.store.getSnapshot().accessToken
      if (token) config.headers.set('Authorization', `Bearer ${token}`)
      return config
    })

    this.http.interceptors.response.use(
      (response) => response,
      async (error) => {
        const original = error.config as RetryableConfig | undefined
        if (error.response?.status === 401 && original && !original._retry && !isAuthEndpoint(original.url)) {
          original._retry = true
          const newToken = await this.tryRefresh()
          if (newToken) {
            original.headers.set('Authorization', `Bearer ${newToken}`)
            return this.http.request(original)
          }
          this.clearSession()
        }
        return Promise.reject(error)
      },
    )
  }

  /** Deduplica refresh concurrentes: si dos requests pisan un 401 al mismo tiempo, comparten un solo refresh. */
  private tryRefresh(): Promise<string | null> {
    this.refreshPromise ??= (async () => {
      const refreshToken = await this.storage.getRefreshToken()
      if (!refreshToken) return null
      try {
        const result = await authApi.refresh(this.http, { refreshToken })
        this.applySession(result)
        return result.accessToken ?? null
      } catch (error) {
        // El servidor lo rechazó (vencido, revocado o de otro backend): no sirve más y reintentarlo en
        // cada arranque sólo demoraría la app. Sin respuesta (sin red, timeout) se conserva, porque
        // puede seguir siendo válido cuando vuelva la conexión.
        if (isAxiosError(error) && error.response && error.response.status < 500) {
          await this.storage.clearRefreshToken()
        }
        return null
      } finally {
        this.refreshPromise = null
      }
    })()
    return this.refreshPromise
  }

  /**
   * Otros flujos que devuelven tokens sin pasar por /api/auth/login (ej. registro de Provider) usan esto
   * para hidratar la sesión de la misma forma, sin duplicar la lógica de applySession.
   */
  applyExternalSession(result: AuthResultResponse) {
    this.applySession(result)
  }

  private applySession(result: AuthResultResponse) {
    if (result.refreshToken) void this.storage.setRefreshToken(result.refreshToken)
    this.store.setState({
      status: 'authenticated',
      accessToken: result.accessToken ?? null,
      user: result.user ?? null,
    })
  }

  private clearSession() {
    void this.storage.clearRefreshToken()
    this.store.setState({ status: 'unauthenticated', accessToken: null, user: null })
  }

  async login(body: LoginRequest) {
    this.store.setState({ status: 'loading' })
    try {
      const result = await authApi.login(this.http, body)
      this.applySession(result)
      return result
    } catch (error) {
      this.store.setState({ status: 'unauthenticated' })
      throw error
    }
  }

  /** Al abrir la app: si hay un refresh token guardado, intenta restaurar sesión silenciosamente. */
  async bootstrap() {
    const refreshToken = await this.storage.getRefreshToken()
    if (!refreshToken) {
      this.store.setState({ status: 'unauthenticated' })
      return
    }
    this.store.setState({ status: 'loading' })
    const token = await this.tryRefresh()
    if (!token) this.store.setState({ status: 'unauthenticated', accessToken: null, user: null })
  }

  async logout() {
    const refreshToken = await this.storage.getRefreshToken()
    try {
      if (refreshToken) await authApi.logout(this.http, { refreshToken })
    } finally {
      this.clearSession()
    }
  }
}
