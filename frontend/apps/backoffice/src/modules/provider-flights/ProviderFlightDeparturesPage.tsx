import { formatCurrency } from '@turisclick/utils'
import type { providerFlightsApi } from '@turisclick/api-client'
import { ArrowLeft, CalendarPlus, Pencil, Trash2, XCircle } from 'lucide-react'
import { useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { KNOWN_CURRENCIES } from '@/modules/experiences/currencies'
import { ConfirmActionDialog } from '@/modules/platform/ConfirmActionDialog'
import { useCloseDeparture, useCreateDeparture, useDeleteDeparture, useDepartures, useMyFlight, useUpdateDeparture } from './api'
import { DemoBadge } from './DemoBadge'
import { FlightFeatureState } from './FlightFeatureState'
import { flightErrorKind, flightErrorMessage } from './flightErrors'
import { type DepartureFormValues, EMPTY_DEPARTURE, fromDeparture, toDepartureRequest, validateDeparture } from './departureForm'
import { departureStatusLabel, departureStatusVariant, flightCode, formatLocalDateTime } from './labels'

type Departure = providerFlightsApi.ProviderFlightDepartureResponse

export function ProviderFlightDeparturesPage() {
  const { id: flightId } = useParams<{ id: string }>()
  const flight = useMyFlight(flightId)
  const departures = useDepartures(flightId)
  const closeDeparture = useCloseDeparture(flightId ?? '')
  const deleteDeparture = useDeleteDeparture(flightId ?? '')
  const [editing, setEditing] = useState<Departure | 'new' | null>(null)
  const [confirm, setConfirm] = useState<{ kind: 'close' | 'delete'; departure: Departure } | null>(null)
  const [confirmError, setConfirmError] = useState<string | null>(null)

  if (flight.isPending) return <FullScreenSpinner />
  const blockingError = [flight.error, departures.error].find((e) => e && flightErrorKind(e) !== 'other')
  if (blockingError) return <FlightFeatureState error={blockingError} />

  const f = flight.data
  const onConfirm = async () => {
    if (!confirm) return
    setConfirmError(null)
    try {
      if (confirm.kind === 'close') await closeDeparture.mutateAsync(confirm.departure.id!)
      else await deleteDeparture.mutateAsync(confirm.departure.id!)
      setConfirm(null)
    } catch (error) {
      setConfirmError(flightErrorMessage(error))
    }
  }

  return (
    <div>
      <Link
        to="/provider/flights"
        className="mb-4 inline-flex items-center gap-1.5 text-sm font-medium text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver a Vuelos
      </Link>
      <PageHeader
        title={f ? `Salidas · ${f.originIata} → ${f.destinationIata}` : 'Salidas'}
        description={
          f
            ? `${f.carrierName} ${flightCode(f.carrierIata, f.flightNumber)}. Cada salida tiene su fecha y hora local, sus asientos y sus tarifas por tipo de pasajero.`
            : undefined
        }
        actions={
          <>
            <DemoBadge />
            <Button onClick={() => setEditing('new')}>
              <CalendarPlus className="h-4 w-4" aria-hidden="true" />
              Nueva salida
            </Button>
          </>
        }
      />

      {flight.isError && <Alert variant="destructive" className="mb-4">{flightErrorMessage(flight.error)}</Alert>}
      {departures.isError && <Alert variant="destructive" className="mb-4">{flightErrorMessage(departures.error)}</Alert>}

      <Card>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Salida</TableHead>
              <TableHead>Llegada</TableHead>
              <TableHead>Asientos</TableHead>
              <TableHead>Adulto · Niño · Infante</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {departures.isPending && <TableLoadingRow colSpan={6} />}
            {departures.isSuccess && departures.data.length === 0 && (
              <TableEmptyRow
                colSpan={6}
                icon={CalendarPlus}
                title="Este vuelo todavía no tiene salidas"
                description="Sin al menos una salida futura con asientos no se puede publicar."
              />
            )}
            {departures.data?.map((d) => {
              const sold = (d.totalSeats ?? 0) - (d.availableSeats ?? 0)
              const currency = d.currency ?? 'BOB'
              return (
                <TableRow key={d.id}>
                  <TableCell className="font-medium">{formatLocalDateTime(d.departureAt)}</TableCell>
                  <TableCell>{formatLocalDateTime(d.arrivalAt)}</TableCell>
                  <TableCell>
                    {d.availableSeats} libres de {d.totalSeats}
                    {sold > 0 && <span className="block text-xs text-muted-foreground">{sold} vendidos o retenidos</span>}
                  </TableCell>
                  <TableCell>
                    {formatCurrency(d.adultPrice ?? 0, currency)} · {formatCurrency(d.childPrice ?? 0, currency)} ·{' '}
                    {formatCurrency(d.infantPrice ?? 0, currency)}
                  </TableCell>
                  <TableCell>
                    <Badge variant={departureStatusVariant[d.status ?? ''] ?? 'neutral'}>
                      {departureStatusLabel[d.status ?? ''] ?? d.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right">
                    <div className="flex flex-wrap justify-end gap-2">
                      {d.status === 'OPEN' && (
                        <>
                          <Button variant="outline" size="sm" onClick={() => setEditing(d)} aria-label={`Editar la salida del ${formatLocalDateTime(d.departureAt)}`}>
                            <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                            Editar
                          </Button>
                          <Button variant="outline" size="sm" onClick={() => setConfirm({ kind: 'close', departure: d })} aria-label={`Cerrar la venta de la salida del ${formatLocalDateTime(d.departureAt)}`}>
                            <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
                            Cerrar venta
                          </Button>
                        </>
                      )}
                      {!d.hasReservations && (
                        <Button variant="ghost" size="sm" onClick={() => setConfirm({ kind: 'delete', departure: d })} aria-label={`Borrar la salida del ${formatLocalDateTime(d.departureAt)}`}>
                          <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                          Borrar
                        </Button>
                      )}
                    </div>
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </Card>
      <p className="mt-4 text-xs text-muted-foreground">
        Las horas son locales de cada aeropuerto. Una salida con reservas no se borra: se cierra la venta, y sus
        horarios y tarifas quedan fijos.
      </p>

      <Dialog open={!!editing} onOpenChange={(open) => !open && setEditing(null)}>
        {editing && flightId && (
          <DepartureDialogContent
            flightId={flightId}
            departure={editing === 'new' ? null : editing}
            onClose={() => setEditing(null)}
          />
        )}
      </Dialog>

      <ConfirmActionDialog
        open={!!confirm}
        title={confirm?.kind === 'close' ? '¿Cerrar la venta de esta salida?' : '¿Borrar esta salida?'}
        confirmLabel={confirm?.kind === 'close' ? 'Cerrar venta' : 'Borrar'}
        destructive={confirm?.kind === 'delete'}
        pending={closeDeparture.isPending || deleteDeparture.isPending}
        error={confirmError}
        onConfirm={() => void onConfirm()}
        onClose={() => {
          setConfirm(null)
          setConfirmError(null)
        }}
      >
        {confirm?.kind === 'close' ? (
          <p>Deja de venderse. Las reservas que ya tiene se mantienen y siguen necesitando su emisión.</p>
        ) : (
          <p>Se borra la salida. Sólo es posible porque todavía no tiene reservas.</p>
        )}
      </ConfirmActionDialog>
    </div>
  )
}

function DepartureDialogContent({
  flightId,
  departure,
  onClose,
}: {
  flightId: string
  departure: Departure | null
  onClose: () => void
}) {
  const create = useCreateDeparture(flightId)
  const update = useUpdateDeparture(flightId)
  const [values, setValues] = useState<DepartureFormValues>(departure ? fromDeparture(departure) : EMPTY_DEPARTURE)
  const [submitted, setSubmitted] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const submittingRef = useRef(false)
  const errors = submitted ? validateDeparture(values) : {}
  const pending = create.isPending || update.isPending
  const set = (key: keyof DepartureFormValues, value: string) => setValues((c) => ({ ...c, [key]: value }))

  const onSave = async () => {
    setSubmitted(true)
    setError(null)
    if (Object.keys(validateDeparture(values)).length > 0 || submittingRef.current) return
    submittingRef.current = true
    try {
      if (departure) await update.mutateAsync({ id: departure.id!, body: toDepartureRequest(values) })
      else await create.mutateAsync(toDepartureRequest(values))
      onClose()
    } catch (err) {
      setError(flightErrorMessage(err))
    } finally {
      submittingRef.current = false
    }
  }

  return (
    <DialogContent className="max-w-lg">
      <DialogHeader>
        <DialogTitle>{departure ? 'Editar salida' : 'Nueva salida'}</DialogTitle>
        <DialogDescription>
          Hora local de cada aeropuerto. Los infantes viajan en brazos y no ocupan asiento.
        </DialogDescription>
      </DialogHeader>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Field id="dep-at" label="Salida" error={errors.departureAt}>
          <Input id="dep-at" type="datetime-local" value={values.departureAt} onChange={(e) => set('departureAt', e.target.value)} />
        </Field>
        <Field id="arr-at" label="Llegada" error={errors.arrivalAt}>
          <Input id="arr-at" type="datetime-local" value={values.arrivalAt} onChange={(e) => set('arrivalAt', e.target.value)} />
        </Field>
        <Field id="seats" label="Asientos" error={errors.totalSeats}>
          <Input id="seats" type="number" min={1} max={500} value={values.totalSeats} onChange={(e) => set('totalSeats', e.target.value)} />
        </Field>
        <div className="flex flex-col gap-1.5">
          <Label>Moneda</Label>
          <Select value={values.currency} onValueChange={(v) => set('currency', v)}>
            <SelectTrigger aria-label="Moneda">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {KNOWN_CURRENCIES.map((c) => (
                <SelectItem key={c} value={c}>
                  {c}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {errors.currency && <p className="text-xs text-destructive">{errors.currency}</p>}
        </div>
        <Field id="adult" label="Tarifa adulto" error={errors.adultPrice}>
          <Input id="adult" type="number" min={0} step="0.01" value={values.adultPrice} onChange={(e) => set('adultPrice', e.target.value)} />
        </Field>
        <Field id="child" label="Tarifa niño" error={errors.childPrice}>
          <Input id="child" type="number" min={0} step="0.01" value={values.childPrice} onChange={(e) => set('childPrice', e.target.value)} />
        </Field>
        <Field id="infant" label="Tarifa infante" error={errors.infantPrice}>
          <Input id="infant" type="number" min={0} step="0.01" value={values.infantPrice} onChange={(e) => set('infantPrice', e.target.value)} />
        </Field>
      </div>
      {departure?.hasReservations && (
        <p className="text-xs text-muted-foreground">
          Esta salida ya tiene reservas: los horarios y las tarifas no se pueden cambiar, y los asientos no pueden
          bajar de los ya vendidos.
        </p>
      )}
      {error && <Alert variant="destructive">{error}</Alert>}
      <DialogFooter>
        <Button variant="outline" onClick={onClose} disabled={pending}>
          Cancelar
        </Button>
        <Button loading={pending} onClick={() => void onSave()}>
          {departure ? 'Guardar' : 'Agregar salida'}
        </Button>
      </DialogFooter>
    </DialogContent>
  )
}

function Field({ id, label, error, children }: { id: string; label: string; error?: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  )
}
