import { zodResolver } from '@hookform/resolvers/zod'
import { formatDate } from '@turisclick/utils'
import { ArrowLeft, CalendarClock } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useParams } from 'react-router-dom'
import { z } from 'zod'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { useMyExperience } from '@/modules/experiences/api'
import { getErrorMessage } from '@/lib/errors'
import { useCreateAvailability, useOwnedAvailability } from './api'

const schema = z.object({
  date: z.string().min(1, 'Elegí una fecha.'),
  startTime: z.string().optional().or(z.literal('')),
  totalSlots: z.coerce.number().int().min(1).max(10_000),
})
// z.coerce hace que el tipo de entrada (lo que RHF maneja en los inputs) difiera del de salida
// (lo que recibe el submit ya validado) — RHF necesita los dos generics para no chocar de tipos.
type FormInput = z.input<typeof schema>
type FormOutput = z.output<typeof schema>

const statusVariant: Record<string, 'success' | 'neutral'> = { OPEN: 'success', CLOSED: 'neutral' }

export function ExperienceAvailabilityPage() {
  const { id } = useParams<{ id: string }>()
  const { data: experience } = useMyExperience(id)
  const { data: slots = [], isLoading } = useOwnedAvailability(id!)
  const createMutation = useCreateAvailability(id!)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const form = useForm<FormInput, unknown, FormOutput>({
    resolver: zodResolver(schema),
    defaultValues: { date: '', startTime: '', totalSlots: 10 },
  })

  const onCreate = async (values: FormOutput) => {
    setSubmitError(null)
    try {
      await createMutation.mutateAsync({
        date: values.date,
        startTime: values.startTime || undefined,
        totalSlots: values.totalSlots,
      })
      form.reset({ date: '', startTime: '', totalSlots: values.totalSlots })
    } catch (error) {
      setSubmitError(getErrorMessage(error))
    }
  }

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
        description="UC-P-10. Un slot por fecha (opcionalmente con hora); vacío = día completo."
      />

      <Card className="mb-6">
        <CardHeader>
          <CardTitle className="text-base">Nuevo slot</CardTitle>
        </CardHeader>
        <CardContent>
          <form className="flex flex-wrap items-end gap-4" onSubmit={form.handleSubmit(onCreate)} noValidate>
            <div className="flex flex-col gap-1.5">
              <Label>Fecha</Label>
              <Input type="date" {...form.register('date')} />
              {form.formState.errors.date && <p className="text-xs text-destructive">{form.formState.errors.date.message}</p>}
            </div>
            <div className="flex flex-col gap-1.5">
              <Label>Hora (opcional — vacío = día completo)</Label>
              <Input type="time" {...form.register('startTime')} />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label>Cupos totales</Label>
              <Input type="number" min="1" className="w-32" {...form.register('totalSlots')} />
            </div>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              Crear slot
            </Button>
          </form>
          {submitError && (
            <Alert variant="destructive" className="mt-4">
              {submitError}
            </Alert>
          )}
        </CardContent>
      </Card>

      <Card>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Fecha</TableHead>
              <TableHead>Hora</TableHead>
              <TableHead>Cupos totales</TableHead>
              <TableHead>Retenidos</TableHead>
              <TableHead>Disponibles</TableHead>
              <TableHead>Estado</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={6} />}
            {!isLoading && slots.length === 0 && (
              <TableEmptyRow
                colSpan={6}
                icon={CalendarClock}
                title="Todavía no hay slots de disponibilidad"
                description="Creá el primero con el formulario de arriba."
              />
            )}
            {slots.map((slot) => (
              <TableRow key={slot.id}>
                <TableCell>{slot.date ? formatDate(slot.date) : '—'}</TableCell>
                <TableCell className="text-muted-foreground">{slot.startTime ?? 'Día completo'}</TableCell>
                <TableCell>{slot.totalSlots}</TableCell>
                <TableCell>{slot.reservedSlots}</TableCell>
                <TableCell className="font-medium">{slot.availableSlots}</TableCell>
                <TableCell>
                  <Badge variant={statusVariant[slot.status ?? ''] ?? 'neutral'}>{slot.status === 'OPEN' ? 'Abierto' : 'Cerrado'}</Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>
    </div>
  )
}
