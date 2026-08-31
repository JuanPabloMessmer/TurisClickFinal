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
import { useMyPackage } from '@/modules/packages/api'
import { getErrorMessage } from '@/lib/errors'
import { useCreatePackageAvailability, useOwnedPackageAvailability } from './api'

const schema = z.object({
  departureDate: z.string().min(1, 'Elegí una fecha de salida.'),
  totalSlots: z.coerce.number().int().min(1).max(10_000),
})
// z.coerce hace que el tipo de entrada (lo que RHF maneja en los inputs) difiera del de salida
// (lo que recibe el submit ya validado) — RHF necesita los dos generics para no chocar de tipos.
type FormInput = z.input<typeof schema>
type FormOutput = z.output<typeof schema>

const statusVariant: Record<string, 'success' | 'neutral'> = { OPEN: 'success', CLOSED: 'neutral' }

export function PackageAvailabilityPage() {
  const { id } = useParams<{ id: string }>()
  const { data: pkg } = useMyPackage(id)
  const { data: departures = [], isLoading } = useOwnedPackageAvailability(id!)
  const createMutation = useCreatePackageAvailability(id!)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const form = useForm<FormInput, unknown, FormOutput>({
    resolver: zodResolver(schema),
    defaultValues: { departureDate: '', totalSlots: 10 },
  })

  const onCreate = async (values: FormOutput) => {
    setSubmitError(null)
    try {
      await createMutation.mutateAsync({ departureDate: values.departureDate, totalSlots: values.totalSlots })
      form.reset({ departureDate: '', totalSlots: values.totalSlots })
    } catch (error) {
      setSubmitError(getErrorMessage(error))
    }
  }

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
        title={`Disponibilidad — ${pkg?.title ?? '…'}`}
        description="UC-P-11. Cada salida es una fecha concreta con cupos totales para el paquete completo."
      />

      <Card className="mb-6">
        <CardHeader>
          <CardTitle className="text-base">Nueva salida</CardTitle>
        </CardHeader>
        <CardContent>
          <form className="flex flex-wrap items-end gap-4" onSubmit={form.handleSubmit(onCreate)} noValidate>
            <div className="flex flex-col gap-1.5">
              <Label>Fecha de salida</Label>
              <Input type="date" {...form.register('departureDate')} />
              {form.formState.errors.departureDate && (
                <p className="text-xs text-destructive">{form.formState.errors.departureDate.message}</p>
              )}
            </div>
            <div className="flex flex-col gap-1.5">
              <Label>Cupos totales</Label>
              <Input type="number" min="1" className="w-32" {...form.register('totalSlots')} />
            </div>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              Crear salida
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
              <TableHead>Fecha de salida</TableHead>
              <TableHead>Cupos totales</TableHead>
              <TableHead>Retenidos</TableHead>
              <TableHead>Disponibles</TableHead>
              <TableHead>Estado</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={5} />}
            {!isLoading && departures.length === 0 && (
              <TableEmptyRow
                colSpan={5}
                icon={CalendarClock}
                title="Todavía no hay salidas programadas"
                description="Creá la primera con el formulario de arriba."
              />
            )}
            {departures.map((departure) => (
              <TableRow key={departure.id}>
                <TableCell>{departure.departureDate ? formatDate(departure.departureDate) : '—'}</TableCell>
                <TableCell>{departure.totalSlots}</TableCell>
                <TableCell>{departure.reservedSlots}</TableCell>
                <TableCell className="font-medium">{departure.availableSlots}</TableCell>
                <TableCell>
                  <Badge variant={statusVariant[departure.status ?? ''] ?? 'neutral'}>
                    {departure.status === 'OPEN' ? 'Abierta' : 'Cerrada'}
                  </Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>
    </div>
  )
}
