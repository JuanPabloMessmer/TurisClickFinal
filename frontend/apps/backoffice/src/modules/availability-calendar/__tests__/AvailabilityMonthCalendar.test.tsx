import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AvailabilityMonthCalendar, type CalendarSlot } from '@/modules/availability-calendar/AvailabilityMonthCalendar'
import { EditSlotDialog } from '@/modules/availability-calendar/EditSlotDialog'

/** Fechas lejanas: el calendario abre en el primer mes con disponibilidad futura y no depende de "hoy". */
const slots: CalendarSlot[] = [
  { id: 's1', date: '2027-05-03', time: '09:00:00', totalSlots: 10, reservedSlots: 4, availableSlots: 6, status: 'OPEN' },
  { id: 's2', date: '2027-05-03', time: '15:00:00', totalSlots: 8, reservedSlots: 8, availableSlots: 0, status: 'OPEN' },
  { id: 's3', date: '2027-05-04', time: null, totalSlots: 12, reservedSlots: 0, availableSlots: 12, status: 'CLOSED' },
]

describe('calendario del proveedor', () => {
  it('muestra cada fecha con sus horarios, cupos y estado', () => {
    render(<AvailabilityMonthCalendar slots={slots} isLoading={false} onSelect={vi.fn()} />)

    expect(screen.getByText('Mayo 2027')).toBeTruthy()
    expect(screen.getByText(/3 disponibilidades/)).toBeTruthy()
    expect(screen.getByText(/12 lugares reservados/)).toBeTruthy()
    expect(screen.getByLabelText('2027-05-03 09:00: 6 de 10 libres')).toBeTruthy()
    expect(screen.getByLabelText('2027-05-03 15:00: 0 de 8 libres')).toBeTruthy()
    expect(screen.getByLabelText('2027-05-04 día completo: 12 de 12 libres, cerrada')).toBeTruthy()
  })

  it('tocar una fecha la abre para editar', () => {
    const onSelect = vi.fn()
    render(<AvailabilityMonthCalendar slots={slots} isLoading={false} onSelect={onSelect} />)

    fireEvent.click(screen.getByLabelText('2027-05-03 09:00: 6 de 10 libres'))

    expect(onSelect).toHaveBeenCalledWith(slots[0])
  })

  it('navega entre meses', () => {
    render(<AvailabilityMonthCalendar slots={slots} isLoading={false} onSelect={vi.fn()} />)

    fireEvent.click(screen.getByLabelText('Mes siguiente'))
    expect(screen.getByText('Junio 2027')).toBeTruthy()
    expect(screen.getByText(/0 disponibilidades/)).toBeTruthy()

    fireEvent.click(screen.getByLabelText('Mes anterior'))
    expect(screen.getByText('Mayo 2027')).toBeTruthy()
  })
})

describe('edición de una fecha', () => {
  it('no deja bajar el cupo por debajo de lo ya reservado', () => {
    const save = vi.fn().mockResolvedValue(undefined)
    render(<EditSlotDialog slot={slots[0]} onClose={vi.fn()} save={save} />)

    expect(screen.getByText('4 reservados · 6 libres · Abierta')).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Capacidad total'), { target: { value: '2' } })

    expect(screen.getByText('No puede ser menor que los 4 lugares ya reservados.')).toBeTruthy()
    expect((screen.getByText('Guardar capacidad') as HTMLButtonElement).disabled).toBe(true)
  })

  it('guarda una capacidad válida y avisa que cerrar respeta las reservas', () => {
    const save = vi.fn().mockResolvedValue(undefined)
    render(<EditSlotDialog slot={slots[0]} onClose={vi.fn()} save={save} />)

    expect(screen.getByText(/las 4 plazas reservadas se mantienen/)).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Capacidad total'), { target: { value: '20' } })
    fireEvent.click(screen.getByText('Guardar capacidad'))

    expect(save).toHaveBeenCalledWith(slots[0], { totalSlots: 20 })
  })

  it('una fecha cerrada ofrece reabrirla', () => {
    const save = vi.fn().mockResolvedValue(undefined)
    render(<EditSlotDialog slot={slots[2]} onClose={vi.fn()} save={save} />)

    fireEvent.click(screen.getByText('Reabrir fecha'))

    expect(save).toHaveBeenCalledWith(slots[2], { status: 'OPEN' })
  })
})
