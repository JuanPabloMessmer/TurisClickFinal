import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CancellationPolicySection, validate } from '@/modules/packages/CancellationPolicySection'

/**
 * La política de cancelación del paquete, desde el lado del operador.
 *
 * Lo que se protege: que no ofrecer cancelación sea una opción explícita y no un formulario a medio llenar,
 * que una política contradictoria se avise antes de guardar, y que quede claro que editarla no cambia lo que
 * ya se vendió.
 */
describe('política de cancelación', () => {
  it('sin tramos explica que una reserva pagada no se cancelará desde la app', () => {
    render(<CancellationPolicySection tiers={[]} onChange={vi.fn()} />)

    expect(screen.getByText(/no se puede cancelar desde la app/)).toBeTruthy()
    // No hay tramos que editar hasta que el operador lo habilite.
    expect(screen.queryByLabelText('Días antes de la salida')).toBeNull()
  })

  it('al habilitarla arranca con la política más común en vez de un formulario vacío', () => {
    const onChange = vi.fn()
    render(<CancellationPolicySection tiers={[]} onChange={onChange} />)

    fireEvent.click(screen.getByRole('checkbox'))

    expect(onChange).toHaveBeenCalledWith([
      { minDaysBefore: 30, refundPercentage: 100 },
      { minDaysBefore: 15, refundPercentage: 50 },
      { minDaysBefore: 0, refundPercentage: 0 },
    ])
  })

  it('muestra en palabras lo que el viajero va a leer', () => {
    render(
      <CancellationPolicySection
        tiers={[
          { minDaysBefore: 30, refundPercentage: 100 },
          { minDaysBefore: 15, refundPercentage: 50 },
          { minDaysBefore: 0, refundPercentage: 0 },
        ]}
        onChange={vi.fn()}
      />,
    )

    expect(screen.getAllByText('30 días o más antes: se devuelve todo').length).toBeGreaterThan(0)
    expect(screen.getAllByText('15 días o más antes: se devuelve el 50%').length).toBeGreaterThan(0)
    // El tramo de 0 días nombra el umbral real en vez de mandar a resolver "los días del tramo anterior".
    expect(screen.getAllByText('Menos de 15 días antes: no se devuelve nada').length).toBeGreaterThan(0)
    expect(screen.getByText(/no afecta a las reservas que ya existen/)).toBeTruthy()
  })

  it('avisa cuando un tramo más cercano devuelve más que uno más lejano', () => {
    render(
      <CancellationPolicySection
        tiers={[
          { minDaysBefore: 30, refundPercentage: 50 },
          { minDaysBefore: 10, refundPercentage: 100 },
        ]}
        onChange={vi.fn()}
      />,
    )

    expect(screen.getByText(/no puede devolver más que uno más lejano/)).toBeTruthy()
  })

  it('avisa cuando dos tramos empiezan el mismo día', () => {
    render(
      <CancellationPolicySection
        tiers={[
          { minDaysBefore: 30, refundPercentage: 100 },
          { minDaysBefore: 30, refundPercentage: 50 },
        ]}
        onChange={vi.fn()}
      />,
    )

    expect(screen.getByText(/misma cantidad de días/)).toBeTruthy()
  })

  it('una política coherente no tiene avisos', () => {
    expect(
      validate([
        { minDaysBefore: 45, refundPercentage: 100 },
        { minDaysBefore: 20, refundPercentage: 60 },
        { minDaysBefore: 0, refundPercentage: 0 },
      ]),
    ).toEqual([])

    // Y no ofrecer cancelación tampoco lo es: es una decisión válida.
    expect(validate([])).toEqual([])
  })
})
