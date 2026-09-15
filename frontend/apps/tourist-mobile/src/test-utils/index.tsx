import type { ReservationItemResponse, ReservationResponse } from '@turisclick/api-client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AxiosError, AxiosHeaders } from 'axios'
import type { useSession } from '@/auth/session'

/**
 * Utilidades compartidas por los tests de Fase 2. Los `jest.mock` quedan en cada archivo de test (Jest los
 * iza por archivo); acá solo viven fábricas de datos y respuestas.
 */

export type SessionValue = ReturnType<typeof useSession>

/**
 * QueryClient de test. Las mutations tienen `retry: 3` A PROPÓSITO: así, si una mutation no reintenta, es
 * porque el hook lo impide explícitamente y no por el default de la librería.
 */
export function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: Infinity },
      mutations: { retry: 3, retryDelay: 0 },
    },
  })
}

export function withClient(client: QueryClient) {
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>
  }
}

export function sessionValue(overrides: Partial<SessionValue> = {}): SessionValue {
  return {
    status: 'unauthenticated',
    user: null,
    isAuthenticated: false,
    login: jest.fn(),
    register: jest.fn(),
    logout: jest.fn(),
    ...overrides,
  }
}

export const guestSession = () => sessionValue()

export function touristSession(id = 'user-a', fullName = 'Ana Quispe'): SessionValue {
  return sessionValue({
    status: 'authenticated',
    isAuthenticated: true,
    user: { id, fullName, firstName: fullName.split(' ')[0], email: `${id}@example.com`, role: 'TOURIST' },
  })
}

export function ok<T>(data: T) {
  return Promise.resolve({ data })
}

export function httpError(status: number, data?: unknown) {
  const config = { headers: new AxiosHeaders() }
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', config as never, {}, {
    status,
    statusText: '',
    data,
    headers: {},
    config: config as never,
  })
}

export function networkError() {
  return new AxiosError('Network Error', 'ERR_NETWORK')
}

/** Promesa controlable desde el test, para inspeccionar la UI mientras una request está en vuelo. */
export function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

export function itemFixture(overrides: Partial<ReservationItemResponse> = {}): ReservationItemResponse {
  return {
    id: 'item-1',
    reservationId: 'r1',
    productType: 'EXPERIENCE',
    experienceId: 'e1',
    experienceTitle: 'Tour Illimani',
    packageId: null,
    packageTitle: null,
    companyName: 'Andes Tours',
    travelers: 2,
    unitPrice: 40,
    currency: 'USD',
    subtotal: 80,
    status: 'PENDING_PAYMENT',
    cancelledAt: null,
    cancellationReason: null,
    date: '2026-10-10',
    startTime: '09:00:00',
    priceChanged: false,
    currentUnitPrice: null,
    currentCurrency: null,
    ...overrides,
  }
}

export function reservationFixture(overrides: Partial<ReservationResponse> = {}): ReservationResponse {
  return {
    id: 'r1',
    status: 'PENDING_PAYMENT',
    expiresAt: new Date(Date.now() + 20 * 60_000).toISOString(),
    createdAt: new Date().toISOString(),
    confirmedAt: null,
    cancelledAt: null,
    items: [itemFixture()],
    totals: [{ currency: 'USD', amount: 80 }],
    requiresPriceAcceptance: false,
    paymentApproved: null,
    paymentFailureReason: null,
    ...overrides,
  }
}

export function pagedFixture(items: ReservationResponse[], page = 1, totalPages = 1) {
  return { items, page, pageSize: 20, totalCount: items.length, totalPages }
}
