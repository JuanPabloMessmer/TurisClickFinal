import { render, screen } from '@testing-library/react-native'
import { FlightBookingCard } from '@/features/flights/components'
import { flightFixture } from '@/test-utils'

/**
 * El vuelo de una reserva. Lo que se protege: **nunca decir "pasaje emitido"**. Que el proveedor confirme
 * una orden no es un pasaje emitido, TurisClick todavía no registra emisiones, y una reserva simulada o de
 * prueba no tiene ni pasaje ni asiento detrás — la tarjeta lo tiene que decir.
 */

const confirmed = (inventoryMode?: string) =>
  flightFixture({ status: 'CONFIRMED', bookingReference: 'FAKE0UYU', inventoryMode, statusMessage: 'x' })

describe('tarjeta del vuelo de una reserva', () => {
  it('una reserva simulada se llama así, con un aviso de que no hay pasaje real', () => {
    render(<FlightBookingCard flight={confirmed('SIMULATED')} />)

    expect(screen.getByText('Reserva simulada')).toBeTruthy()
    expect(screen.getByTestId('flight-demo-notice')).toBeTruthy()
    expect(screen.getByText(/No existe un pasaje ni un asiento real/)).toBeTruthy()
    expect(screen.queryByText(/pasaje emitido/i)).toBeNull()
  })

  it('una reserva del entorno de prueba del proveedor se rotula como prueba', () => {
    render(<FlightBookingCard flight={confirmed('SANDBOX')} />)

    expect(screen.getByText('Reserva de prueba')).toBeTruthy()
    expect(screen.getByText(/entorno de prueba del proveedor/)).toBeTruthy()
  })

  it('sin el dato (backend anterior) se asume lo prudente: simulada', () => {
    render(<FlightBookingCard flight={confirmed(undefined)} />)

    expect(screen.getByText('Reserva simulada')).toBeTruthy()
  })

  it('aun en inventario real, confirmar la orden no es "pasaje emitido"', () => {
    render(<FlightBookingCard flight={confirmed('LIVE')} />)

    expect(screen.getByText('Reserva confirmada')).toBeTruthy()
    expect(screen.queryByTestId('flight-demo-notice')).toBeNull()
    expect(screen.queryByText(/pasaje emitido/i)).toBeNull()
  })
})
