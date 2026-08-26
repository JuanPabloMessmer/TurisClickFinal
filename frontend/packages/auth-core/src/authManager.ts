import { authApi, type AuthResultResponse, type LoginRequest } from '@turisclick/api-client'
import type { AxiosInstance, InternalAxiosRequestConfig } from 'axios'
import { AuthStore } from './store'
import type { TokenStorage } from './tokenStorage'

interface RetryableConfig extends InternalAxiosRequestConfig {
  _retry?: boolean
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
        if (error.response?.status === 401 && original && !original._retry) {
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
      } catch {
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
