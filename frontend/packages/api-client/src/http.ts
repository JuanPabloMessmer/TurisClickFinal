import axios, { type AxiosInstance } from 'axios'

export interface HttpClientOptions {
  /** Límite por request en milisegundos. Sin valor, axios espera indefinidamente. */
  timeoutMs?: number
}

/** Cada app pasa su propio baseURL (VITE_API_BASE_URL en Backoffice; EXPO_PUBLIC_API_* en Tourist Mobile). */
export function createHttpClient(baseURL: string, options: HttpClientOptions = {}): AxiosInstance {
  return axios.create({ baseURL, timeout: options.timeoutMs })
}
