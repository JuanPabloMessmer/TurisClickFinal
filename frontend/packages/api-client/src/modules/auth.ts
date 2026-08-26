import type { AxiosInstance } from 'axios'
import type { AuthResultResponse, LoginRequest, LogoutRequest, RefreshTokenRequest } from '../types'

/** UC-AUTH-02. */
export const login = (http: AxiosInstance, body: LoginRequest) =>
  http.post<AuthResultResponse>('/api/auth/login', body).then((r) => r.data)

/** UC-AUTH-03. */
export const refresh = (http: AxiosInstance, body: RefreshTokenRequest) =>
  http.post<AuthResultResponse>('/api/auth/refresh', body).then((r) => r.data)

/** UC-AUTH-04. */
export const logout = (http: AxiosInstance, body: LogoutRequest) => http.post<void>('/api/auth/logout', body)
