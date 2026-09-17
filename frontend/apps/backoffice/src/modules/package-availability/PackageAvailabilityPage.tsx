import { ArrowLeft } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { AvailabilityMonthCalendar, type CalendarSlot } from '@/modules/availability-calendar/AvailabilityMonthCalendar'
import { AvailabilityScheduler } from '@/modules/availability-calendar/AvailabilityScheduler'
import { EditSlotDialog } from '@/modules/availability-calendar/EditSlotDialog'
import { useMyPackage } from '@/modules/packages/api'
import { useBulkCreatePackageAvailability, useOwnedPackageAvailability, useUpdatePackageAvailability } from './api'

/** UC-P-11 con calendario: salidas por rango y patrón semanal (una por fecha, sin horario). */
export function PackageAvailabilityPage() {
  const { id } = useParams<{ id: string }>()
  const { data: pkg } = useMyPackage(id)
  const { data = [], isLoading } = useOwnedPackageAvailability(id!)
  const bulk = useBulkCreatePackageAvailability(id!)
  const update = useUpdatePackageAvailability(id!)
  const [editing, setEditing] = useState<CalendarSlot | null>(null)

  const slots = useMemo<CalendarSlot[]>(
    () =>
      data.flatMap((d) =>
        d.id && d.departureDate
          ? [
              {
                id: d.id,
                date: d.departureDate,
                time: null,
                totalSlots: d.totalSlots ?? 0,
                reservedSlots: d.reservedSlots ?? 0,
                availableSlots: d.availableSlots ?? 0,
                status: d.status ?? 'OPEN',
              },
            ]
          : [],
      ),
    [data],
  )

  return (
    <div>
      <Link
        to="/provider/packages"
        className="mb-4 inline-flex items-center gap-1.5 text-sm font-medium text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver a Paquetes
      </Link>
      <PageHeader
        title={`Salidas — ${pkg?.title ?? '…'}`}
        description="Programá salidas por rango y días de la semana. Las salidas que ya existen no se tocan."
      />

      <AvailabilityScheduler withTimes={false} submit={(body) => bulk.mutateAsync(body)} />

      <AvailabilityMonthCalendar slots={slots} isLoading={isLoading} onSelect={setEditing} />

      <EditSlotDialog
        slot={editing}
        onClose={() => setEditing(null)}
        save={(slot, body) => update.mutateAsync({ availabilityId: slot.id, body })}
      />
    </div>
  )
}
