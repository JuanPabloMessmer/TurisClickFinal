import { ArrowLeft } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { AvailabilityMonthCalendar, type CalendarSlot } from '@/modules/availability-calendar/AvailabilityMonthCalendar'
import { AvailabilityScheduler } from '@/modules/availability-calendar/AvailabilityScheduler'
import { EditSlotDialog } from '@/modules/availability-calendar/EditSlotDialog'
import { useMyExperience } from '@/modules/experiences/api'
import { useBulkCreateAvailability, useOwnedAvailability, useUpdateAvailability } from './api'

/**
 * UC-P-10 con calendario: el proveedor programa rangos por patrón semanal y horarios, y ve/edita cada
 * fecha en una vista mensual. Solo sus propias experiencias: el backend valida ownership en cada llamada.
 */
export function ExperienceAvailabilityPage() {
  const { id } = useParams<{ id: string }>()
  const { data: experience } = useMyExperience(id)
  const { data = [], isLoading } = useOwnedAvailability(id!)
  const bulk = useBulkCreateAvailability(id!)
  const update = useUpdateAvailability(id!)
  const [editing, setEditing] = useState<CalendarSlot | null>(null)

  const slots = useMemo<CalendarSlot[]>(
    () =>
      data.flatMap((s) =>
        s.id && s.date
          ? [
              {
                id: s.id,
                date: s.date,
                time: s.startTime,
                totalSlots: s.totalSlots ?? 0,
                reservedSlots: s.reservedSlots ?? 0,
                availableSlots: s.availableSlots ?? 0,
                status: s.status ?? 'OPEN',
              },
            ]
          : [],
      ),
    [data],
  )

  return (
    <div>
      <Link
        to="/provider/experiences"
        className="mb-4 inline-flex items-center gap-1.5 text-sm font-medium text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver a Experiencias
      </Link>
      <PageHeader
        title={`Disponibilidad — ${experience?.title ?? '…'}`}
        description="Programá fechas por rango y días de la semana. Las fechas que ya existen no se tocan."
      />

      <AvailabilityScheduler withTimes submit={(body) => bulk.mutateAsync(body)} />

      <AvailabilityMonthCalendar slots={slots} isLoading={isLoading} onSelect={setEditing} />

      <EditSlotDialog
        slot={editing}
        onClose={() => setEditing(null)}
        save={(slot, body) => update.mutateAsync({ availabilityId: slot.id, body })}
      />
    </div>
  )
}
