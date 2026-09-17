import {
  addMonths,
  buildMonthGrid,
  groupByDate,
  monthLabel,
  todayIso,
  WEEKDAY_SHORT_LABELS,
  yearMonthOf,
  type YearMonth,
} from '@turisclick/utils'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Spinner } from '@/components/ui/spinner'
import { cn } from '@/lib/utils'

/** Fecha de disponibilidad normalizada (experiencia o salida de paquete). */
export interface CalendarSlot {
  id: string
  date: string
  time?: string | null
  totalSlots: number
  reservedSlots: number
  availableSlots: number
  status: string
}

/**
 * Vista mensual de la disponibilidad propia. Cada día muestra sus horarios con cupo libre/total; las
 * fechas cerradas se ven tachadas y las agotadas en ámbar. Tocar un horario abre su edición.
 */
export function AvailabilityMonthCalendar({
  slots,
  isLoading,
  onSelect,
}: {
  slots: CalendarSlot[]
  isLoading: boolean
  onSelect: (slot: CalendarSlot) => void
}) {
  const today = todayIso()
  const upcoming = slots.map((s) => s.date).filter((d) => d >= today).sort()
  const [month, setMonth] = useState<YearMonth>(() => yearMonthOf(upcoming[0] ?? today))
  const byDate = groupByDate(
    [...slots].sort((a, b) => (a.time ?? '').localeCompare(b.time ?? '')),
    (s) => s.date,
  )
  const inMonth = slots.filter((s) => {
    const ym = yearMonthOf(s.date)
    return ym.year === month.year && ym.month === month.month
  })

  return (
    <Card>
      <CardContent className="p-4">
        <div className="mb-3 flex items-center justify-between">
          <Button variant="ghost" size="icon" aria-label="Mes anterior" onClick={() => setMonth(addMonths(month, -1))}>
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <div className="text-center">
            <h2 className="text-base font-semibold">{monthLabel(month)}</h2>
            <p className="text-xs text-muted-foreground">
              {inMonth.length} {inMonth.length === 1 ? 'disponibilidad' : 'disponibilidades'} ·{' '}
              {inMonth.reduce((sum, s) => sum + s.reservedSlots, 0)} lugares reservados
            </p>
          </div>
          <Button variant="ghost" size="icon" aria-label="Mes siguiente" onClick={() => setMonth(addMonths(month, 1))}>
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>

        {isLoading ? (
          <div className="flex justify-center py-12">
            <Spinner />
          </div>
        ) : (
          <div role="grid" aria-label={monthLabel(month)} className="grid grid-cols-7 gap-1">
            {WEEKDAY_SHORT_LABELS.map((label) => (
              <div key={label} role="columnheader" className="pb-1 text-center text-xs font-semibold text-muted-foreground">
                {label}
              </div>
            ))}
            {buildMonthGrid(month)
              .flat()
              .map((cell) => {
                const daySlots = cell.inMonth ? byDate.get(cell.iso) ?? [] : []
                const past = cell.iso < today
                return (
                  <div
                    key={cell.iso}
                    role="gridcell"
                    className={cn(
                      'min-h-24 rounded-md border p-1.5',
                      !cell.inMonth && 'border-transparent bg-transparent',
                      cell.inMonth && 'border-border bg-surface',
                      cell.inMonth && past && 'bg-muted/40',
                      cell.iso === today && 'border-primary',
                    )}
                  >
                    {cell.inMonth && (
                      <>
                        <div className={cn('mb-1 text-xs font-medium', past ? 'text-muted-foreground' : 'text-foreground')}>{cell.day}</div>
                        <div className="flex flex-col gap-1">
                          {daySlots.map((slot) => {
                            const closed = slot.status === 'CLOSED'
                            const soldOut = !closed && slot.availableSlots <= 0
                            return (
                              <button
                                key={slot.id}
                                type="button"
                                onClick={() => onSelect(slot)}
                                title="Editar cupo o estado"
                                aria-label={`${cell.iso} ${slot.time?.slice(0, 5) ?? 'día completo'}: ${slot.availableSlots} de ${slot.totalSlots} libres${closed ? ', cerrada' : ''}`}
                                className={cn(
                                  'rounded px-1.5 py-0.5 text-left text-[11px] leading-tight transition-colors',
                                  closed && 'bg-muted text-muted-foreground line-through',
                                  soldOut && 'bg-warning/15 text-warning',
                                  !closed && !soldOut && 'bg-primary/10 text-primary hover:bg-primary/20',
                                )}
                              >
                                <span className="font-semibold">{slot.time?.slice(0, 5) ?? 'Día'}</span> {slot.availableSlots}/{slot.totalSlots}
                              </button>
                            )
                          })}
                        </div>
                      </>
                    )}
                  </div>
                )
              })}
          </div>
        )}

        <div className="mt-3 flex flex-wrap gap-4 text-xs text-muted-foreground">
          <span className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded bg-primary/30" /> Abierta (libres/total)</span>
          <span className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded bg-warning/40" /> Agotada</span>
          <span className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded bg-muted" /> Cerrada</span>
        </div>
      </CardContent>
    </Card>
  )
}
