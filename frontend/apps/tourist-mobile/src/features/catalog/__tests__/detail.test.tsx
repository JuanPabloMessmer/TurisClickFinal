import { fireEvent, render, screen } from '@testing-library/react-native'
import { AvailabilityRow, AvailabilitySection, BookingBar, slotsLabel } from '@/features/catalog/detail'

jest.mock('expo-router', () => ({ useRouter: () => ({ back: jest.fn() }) }))

describe('BookingBar', () => {
  /**
   * Fase 1 no tiene booking. Este test existe para que nadie convierta el CTA en algo que parezca
   * funcional sin implementar la reserva de verdad: si el texto o el estado cambian, falla acá.
   */
  it('anuncia que la reserva todavía no está disponible y no se puede presionar', () => {
    const onPress = jest.fn()
    render(<BookingBar amount={350} currency="BOB" priceLabel="Precio por persona" />)

    const cta = screen.getByText('Reservas disponibles próximamente')
    expect(cta).toBeTruthy()

    fireEvent.press(cta)
    expect(onPress).not.toHaveBeenCalled()
    expect(screen.getByRole('button').props.accessibilityState).toMatchObject({ disabled: true })
  })

  it('no dice "Reservar" en ninguna parte', () => {
    render(<BookingBar amount={350} currency="BOB" priceLabel="Precio por persona" />)

    expect(screen.queryByText('Reservar')).toBeNull()
  })

  it('muestra el precio junto a su etiqueta', () => {
    render(<BookingBar amount={350} currency="BOB" priceLabel="Precio por persona" />)

    expect(screen.getByText('Precio por persona')).toBeTruthy()
    expect(screen.getByText(/BOB\s+350\.00/)).toBeTruthy()
  })
})

describe('slotsLabel', () => {
  it('dice "Sin cupo" en vez de "0 lugares"', () => {
    expect(slotsLabel(0)).toBe('Sin cupo')
  })

  it('singulariza el 1', () => {
    expect(slotsLabel(1)).toBe('1 lugar')
    expect(slotsLabel(4)).toBe('4 lugares')
  })

  it('no inventa un número cuando el dato no vino', () => {
    expect(slotsLabel(undefined)).toBe('')
  })
})

describe('AvailabilitySection', () => {
  it('dice explícitamente que no hay fechas en vez de mostrar una lista vacía', () => {
    render(
      <AvailabilitySection isLoading={false} slots={[]}>
        {null}
      </AvailabilitySection>,
    )

    expect(screen.getByText(/No hay fechas con cupo/)).toBeTruthy()
  })

  it('lista las fechas cuando las hay', () => {
    render(
      <AvailabilitySection isLoading={false} slots={[{ id: 'a1' }]}>
        <AvailabilityRow date="2026-10-12" availableSlots={3} />
      </AvailabilitySection>,
    )

    expect(screen.queryByText(/No hay fechas con cupo/)).toBeNull()
    expect(screen.getByText('3 lugares')).toBeTruthy()
  })

  it('mientras carga no muestra ni la lista ni el vacío', () => {
    render(
      <AvailabilitySection isLoading slots={[]}>
        {null}
      </AvailabilitySection>,
    )

    expect(screen.queryByText(/No hay fechas con cupo/)).toBeNull()
  })
})

describe('AvailabilityRow', () => {
  it('recorta los segundos del horario de la experiencia', () => {
    render(<AvailabilityRow date="2026-10-12" time="09:30:00" availableSlots={2} />)

    expect(screen.getByText(/09:30/)).toBeTruthy()
    expect(screen.queryByText(/09:30:00/)).toBeNull()
  })
})
