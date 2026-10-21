import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'

/** Error HTTP con el cuerpo ProblemDetails que devuelve el backend (con `errorCode` cuando lo hay). */
export function httpError(status: number, data: { detail?: string; title?: string; errorCode?: string }) {
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_RESPONSE', undefined, undefined, {
    status,
    statusText: '',
    headers: {},
    config: { headers: new AxiosHeaders() },
    data,
  })
}

export const notAvailable = () =>
  httpError(501, {
    title: 'No implementado',
    detail: 'Los vuelos de operador todavía no están disponibles en este servidor.',
    errorCode: 'FEATURE_NOT_AVAILABLE',
  })

export const notAuthorized = () =>
  httpError(403, { title: 'Acceso denegado', detail: 'La empresa no vende vuelos.', errorCode: 'FLIGHT_SELLER_NOT_AUTHORIZED' })

export function renderWithProviders(ui: ReactNode, route = '/') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
    </QueryClientProvider>,
  )
}
