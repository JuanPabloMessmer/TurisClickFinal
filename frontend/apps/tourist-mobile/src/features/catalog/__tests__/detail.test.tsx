import { fireEvent, render, screen } from '@testing-library/react-native'
import {
  AvailabilityRow,
  AvailabilitySection,
  BookingBar,
  CancellationTerms,
  cancellationTierLabel,
  slotsLabel,
} from '@/features/catalog/detail'

jest.mock('expo-router', () => ({ useRouter: () => ({ back: jest.fn() }) }))

describe('BookingBar', () => {
  /**
   * CAMBIO INTENCIONAL DE FASE 2. En Fase 1 este test fijaba un CTA deshabilitado con el texto "Reservas
   * disponibles próximamente", para que nadie simulara una reserva que no existía. Fase 2 implementa el
   * flujo real, así que el test se reescribió a propósito para fijar el comportamiento nuevo.
   */
  it('ofrece elegir fecha y navega al presionar', () => {
    const onPress = jest.fn()
    render(<BookingBar amount={350} currency="BOB" priceLabel="Precio por persona" ctaLabel="Elegir fecha" onPress={onPress} />)

    fireEvent.press(screen.getByText('Elegir fecha'))

    expect(onPress).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button').props.accessibilityState).toMatchObject({ disabled: false })
  })

  it('ya no anuncia "Reservas disponibles próximamente"', () => {
    render(<BookingBar amount={350} currency="BOB" priceLabel="Precio por persona" ctaLabel="Elegir fecha" onPress={jest.fn()} />)

    expect(screen.queryByText('Reservas disponibles próximamente')).toBeNull()
  })

  it('sin fechas se deshabilita y lo dice, en vez de llevar a una pantalla vacía', () => {
    const onPress = jest.fn()
    render(
      <BookingBar
        amount={350}
        currency="BOB"
        priceLabel="Precio por persona"
        ctaLabel="Sin fechas disponibles"
        disabled
        onPress={onPress}
      />,
    )

    fireEvent.press(screen.getByText('Sin fechas disponibles'))

    expect(onPress).not.toHaveBeenCalled()
    expect(screen.getByRole('button').props.accessibilityState).toMatchObject({ disabled: true })
  })

  it('muestra el precio junto a su etiqueta', () => {
    render(<BookingBar amount={350} currency="BOB" priceLabel="Precio por persona" ctaLabel="Elegir fecha" onPress={jest.fn()} />)

    expect(screen.getByText('Precio por persona')).toBeTruthy()
    expect(screen.getByText(/Bs\s+350,00/)).toBeTruthy()
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

describe('CancellationTerms', () => {
  it('dice cuánto se devuelve y con cuánta anticipación, antes de pagar', () => {
    render(
      <CancellationTerms
        tiers={[
          { minDaysBefore: 0, refundPercentage: 0 },
          { minDaysBefore: 30, refundPercentage: 100 },
          { minDaysBefore: 15, refundPercentage: 40 },
        ]}
      />,
    )

    // Se ordenan de mayor a menor anticipación aunque lleguen desordenados.
    expect(screen.getByText(/30 días o más antes: se devuelve todo/)).toBeTruthy()
    expect(screen.getByText(/15 días o más antes: se devuelve el 40%/)).toBeTruthy()
    // El tramo de 0 días nombra el umbral real, no "los días del tramo anterior".
    expect(screen.getByText(/menos de 15 días: no se devuelve nada/)).toBeTruthy()
  })

  it('sin política, lo dice en vez de dejar suponer que se puede cancelar', () => {
    render(<CancellationTerms tiers={[]} />)

    expect(screen.getByText(/no se cancela desde la app/)).toBeTruthy()
  })
})

describe('cancellationTierLabel', () => {
  it('sin un tramo más exigente arriba, el tramo de 0 días cubre cualquier momento', () => {
    expect(cancellationTierLabel({ minDaysBefore: 0, refundPercentage: 100 })).toBe(
      'Cancelando en cualquier momento: se devuelve todo',
    )
  })

  it('singulariza un solo día', () => {
    expect(cancellationTierLabel({ minDaysBefore: 1, refundPercentage: 50 })).toBe(
      'Cancelando 1 día o más antes: se devuelve el 50%',
    )
  })
})
