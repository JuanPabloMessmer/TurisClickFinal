import { AvailabilityPresets, type AvailabilityPresetValue } from '@turisclick/api-client'
import { formatDate, todayIso, WEEKDAY_INITIALS, WEEKDAY_LONG_LABELS, WEEKDAYS_MONDAY_FIRST } from '@turisclick/utils'
import { CalendarPlus, Plus, X } from 'lucide-react'
import { useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { getErrorMessage } from '@/lib/errors'
import { cn } from '@/lib/utils'
import {
  applyPreset,
  buildBulkRequest,
  estimateSchedule,
  initialScheduleForm,
  toggleWeekday,
  validateSchedule,
  type ScheduleFormState,
} from './scheduleForm'

const PRESET_LABELS: Record<AvailabilityPresetValue, string> = {
  EVERY_DAY: 'Todos los días',
  WEEKDAYS: 'Lunes a viernes',
  WEEKENDS: 'Fines de semana',
  CUSTOM: 'Personalizado',
}

export interface BulkResult {
  createdCount?: number
  skippedCount?: number
  requestedCount?: number
  dryRun?: boolean
}

type BulkBody = ReturnType<typeof buildBulkRequest>

/**
 * "Programar disponibilidad": rango + patrón semanal + horarios + capacidad. Primero se revisa con una
 * vista previa del servidor (dryRun: cuántas se crean y cuántas ya existían) y recién después se confirma.
 * Nunca pisa fechas existentes: el backend las omite y las informa.
 */
export function AvailabilityScheduler({
  withTimes,
  submit,
}: {
  /** true para experiencias (horarios por fecha); false para salidas de paquete. */
  withTimes: boolean
  submit: (body: BulkBody) => Promise<BulkResult>
}) {
  const today = todayIso()
  const [form, setForm] = useState<ScheduleFormState>(() => initialScheduleForm(today))
  const [preview, setPreview] = useState<BulkResult | null>(null)
  const [done, setDone] = useState<BulkResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const update = (next: ScheduleFormState) => {
    setForm(next)
    setPreview(null)
    setDone(null)
    setError(null)
  }

  const estimate = estimateSchedule(form, withTimes)
  const validation = validateSchedule(form, withTimes, today)

  const run = async (dryRun: boolean) => {
    if (validation) {
      setError(validation)
      return
    }
    setBusy(true)
    setError(null)
    try {
      const result = await submit(buildBulkRequest(form, withTimes, dryRun))
      if (dryRun) setPreview(result)
      else {
        setDone(result)
        setPreview(null)
      }
    } catch (e) {
      setError(getErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card className="mb-6">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <CalendarPlus className="h-4 w-4" aria-hidden="true" />
          Programar disponibilidad
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        <div className="flex flex-wrap gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="schedule-start">Desde</Label>
            <Input id="schedule-start" type="date" min={today} value={form.startDate} onChange={(e) => update({ ...form, startDate: e.target.value })} />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="schedule-end">Hasta</Label>
            <Input id="schedule-end" type="date" min={form.startDate || today} value={form.endDate} onChange={(e) => update({ ...form, endDate: e.target.value })} />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="schedule-capacity">Capacidad por {withTimes ? 'horario' : 'salida'}</Label>
            <Input
              id="schedule-capacity"
              type="number"
              min={1}
              max={10000}
              className="w-32"
              value={Number.isNaN(form.totalSlots) ? '' : form.totalSlots}
              onChange={(e) => update({ ...form, totalSlots: e.target.valueAsNumber })}
            />
          </div>
        </div>

        <div className="flex flex-col gap-2">
          <Label>Patrón</Label>
          <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Patrón">
            {AvailabilityPresets.map((preset) => (
              <button
                key={preset}
                type="button"
                role="radio"
                aria-checked={form.preset === preset}
                onClick={() => update(applyPreset(form, preset))}
                className={cn(
                  'rounded-full border px-3.5 py-1.5 text-sm font-medium transition-colors',
                  form.preset === preset ? 'border-primary bg-primary text-primary-foreground' : 'border-border hover:bg-muted',
                )}
              >
                {PRESET_LABELS[preset]}
              </button>
            ))}
          </div>
          <div className="flex gap-1.5" aria-label="Días de la semana">
            {WEEKDAYS_MONDAY_FIRST.map((day) => {
              const on = form.weekdays.includes(day)
              return (
                <button
                  key={day}
                  type="button"
                  aria-pressed={on}
                  aria-label={WEEKDAY_LONG_LABELS[day]}
                  title={WEEKDAY_LONG_LABELS[day]}
                  onClick={() => update(toggleWeekday(form, day))}
                  className={cn(
                    'h-9 w-9 rounded-full border text-sm font-semibold transition-colors',
                    on ? 'border-primary bg-primary/10 text-primary' : 'border-border text-muted-foreground hover:bg-muted',
                  )}
                >
                  {WEEKDAY_INITIALS[day]}
                </button>
              )
            })}
          </div>
        </div>

        {withTimes && (
          <div className="flex flex-col gap-2">
            <Label>Horarios (sin horarios = día completo)</Label>
            <div className="flex flex-wrap items-center gap-2">
              {form.startTimes.map((time, index) => (
                <div key={index} className="flex items-center gap-1">
                  <Input
                    type="time"
                    aria-label={`Horario ${index + 1}`}
                    className="w-32"
                    value={time}
                    onChange={(e) => update({ ...form, startTimes: form.startTimes.map((t, i) => (i === index ? e.target.value : t)) })}
                  />
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label={`Quitar horario ${index + 1}`}
                    onClick={() => update({ ...form, startTimes: form.startTimes.filter((_, i) => i !== index) })}
                  >
                    <X className="h-4 w-4" />
                  </Button>
                </div>
              ))}
              <Button type="button" variant="outline" size="sm" onClick={() => update({ ...form, startTimes: [...form.startTimes, ''] })}>
                <Plus className="h-3.5 w-3.5" /> Agregar horario
              </Button>
            </div>
          </div>
        )}

        <div className="rounded-lg bg-muted/50 px-4 py-3 text-sm">
          {estimate.total === 0 ? (
            <span className="text-muted-foreground">Elegí rango y días para ver cuántas fechas se generan.</span>
          ) : (
            <span>
              Se generarían <strong>{estimate.total}</strong> disponibilidades ({estimate.dates.length} fechas
              {withTimes && estimate.timesPerDate > 1 ? ` × ${estimate.timesPerDate} horarios` : ''}), del{' '}
              {formatDate(estimate.dates[0])} al {formatDate(estimate.dates[estimate.dates.length - 1])}.
            </span>
          )}
        </div>

        {preview && (
          <Alert>
            Vista previa: se crearán <strong>{preview.createdCount}</strong> y se omitirán <strong>{preview.skippedCount}</strong> que ya
            existían (no se modifican, conservan su cupo y sus reservas).
          </Alert>
        )}
        {done && (
          <Alert variant="success">
            Listo: se crearon {done.createdCount} disponibilidades{done.skippedCount ? ` y se omitieron ${done.skippedCount} existentes` : ''}.
          </Alert>
        )}
        {error && <Alert variant="destructive">{error}</Alert>}

        <div className="flex gap-2">
          <Button type="button" variant="outline" disabled={busy} onClick={() => void run(true)}>
            Revisar
          </Button>
          <Button type="button" disabled={busy || !preview || preview.createdCount === 0} onClick={() => void run(false)}>
            {preview ? `Confirmar y crear ${preview.createdCount}` : 'Confirmar'}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
