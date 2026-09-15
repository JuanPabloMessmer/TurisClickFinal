import { render, screen, waitFor } from '@testing-library/react-native'
import { useSession } from '@/auth/session'
import { Checkout } from '@/features/reservations/Checkout'
import { createTestQueryClient, ok, reservationFixture, touristSession, withClient } from '@/test-utils'

/**
 * Archivo aparte porque `IS_DEVELOPMENT` es una constante de módulo: acá se simula un build de
 * producción para fijar que la herramienta de demostración no aparezca.
 */
jest.mock('@/lib/env', () => ({ ...jest.requireActual('@/lib/env'), IS_DEVELOPMENT: false }))

const mockGet = jest.fn()
jest.mock('@/lib/httpClient', () => ({ httpClient: { get: (...args: unknown[]) => mockGet(...args), post: jest.fn() } }))
jest.mock('expo-router', () => ({ useRouter: () => ({ push: jest.fn(), replace: jest.fn(), navigate: jest.fn() }) }))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

it('en producción "Simular rechazo" no existe: solo "Pagar"', async () => {
  ;(useSession as jest.MockedFunction<typeof useSession>).mockReturnValue(touristSession())
  mockGet.mockReturnValue(ok(reservationFixture({ id: 'r1' })))
  const client = createTestQueryClient()
  const Wrapper = withClient(client)

  render(
    <Wrapper>
      <Checkout id="r1" />
    </Wrapper>,
  )

  await waitFor(() => expect(screen.getByRole('button', { name: 'Pagar' })).toBeTruthy())
  expect(screen.queryByText('Simular rechazo')).toBeNull()
  client.clear()
})
