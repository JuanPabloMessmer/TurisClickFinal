import axios, { type AxiosInstance } from 'axios'

/** Cada app pasa su propio baseURL (VITE_API_BASE_URL en Backoffice; su equivalente en Expo más adelante). */
export function createHttpClient(baseURL: string): AxiosInstance {
  return axios.create({ baseURL })
}
