import type { AxiosInstance } from 'axios'
import type {
  AuthResultResponse,
  ChangePasswordRequest,
  LoginRequest,
  LogoutRequest,
  RefreshTokenRequest,
  RegisterTouristRequest,
} from '../types'

/** UC-AUTH-01 — alta autoservicio de TOURIST. Devuelve la sesión ya iniciada, igual que login. */
export const registerTourist = (http: AxiosInstance, body: RegisterTouristRequest) =>
  http.post<AuthResultResponse>('/api/auth/register', body).then((r) => r.data)

/** UC-AUTH-02. */
export const login = (http: AxiosInstance, body: LoginRequest) =>
  http.post<AuthResultResponse>('/api/auth/login', body).then((r) => r.data)

/** UC-AUTH-03. */
export const refresh = (http: AxiosInstance, body: RefreshTokenRequest) =>
  http.post<AuthResultResponse>('/api/auth/refresh', body).then((r) => r.data)

/** UC-AUTH-04. */
export const logout = (http: AxiosInstance, body: LogoutRequest) => http.post<void>('/api/auth/logout', body)

/**
 * Cambia la contraseña de la cuenta autenticada y devuelve una sesión nueva.
 *
 * Es el único endpoint que una cuenta con contraseña temporal puede usar: mientras `user.mustChangePassword`
 * sea true, la API rechaza todo lo demás. El token que devuelve ya no lleva ese bloqueo.
 */
export const changePassword = (http: AxiosInstance, body: ChangePasswordRequest) =>
  http.post<AuthResultResponse>('/api/auth/change-password', body).then((r) => r.data)
