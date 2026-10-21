import { ArrowLeft } from 'lucide-react'
import { useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { CancellationPolicySection } from '@/modules/packages/CancellationPolicySection'
import { AirportField } from './AirportField'
import { useCreateFlight, useMyFlight, useUpdateFlight } from './api'
import { DemoBadge } from './DemoBadge'
import { FlightFeatureState } from './FlightFeatureState'
import { flightErrorKind, flightErrorMessage } from './flightErrors'
import { EMPTY_FLIGHT, type FlightFormValues, fromFlight, toFlightRequest, validateFlight } from './flightForm'
import { CABIN_CLASSES, cabinLabel } from './labels'

/** Carga el vuelo (en edición) y recién entonces monta el formulario, ya inicializado: sin efectos de sincronización. */
export function ProviderFlightFormPage() {
  const { id } = useParams<{ id: string }>()
  const existing = useMyFlight(id)

  if (id && existing.isPending) return <FullScreenSpinner />
  if (id && existing.isError && flightErrorKind(existing.error) !== 'other') return <FlightFeatureState error={existing.error} />

  return <FlightForm key={id ?? 'new'} id={id} initial={existing.data ? fromFlight(existing.data) : EMPTY_FLIGHT} loadError={id ? existing.error : null} />
}

function FlightForm({ id, initial, loadError }: { id?: string; initial: FlightFormValues; loadError: unknown }) {
  const isEdit = !!id
  const navigate = useNavigate()
  const createFlight = useCreateFlight()
  const updateFlight = useUpdateFlight()
  const [values, setValues] = useState<FlightFormValues>(initial)
  const [submitted, setSubmitted] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(loadError ? flightErrorMessage(loadError) : null)
  const submittingRef = useRef(false)

  const errors = submitted ? validateFlight(values) : {}
  const set = <K extends keyof FlightFormValues>(key: K, value: FlightFormValues[K]) =>
    setValues((current) => ({ ...current, [key]: value }))
  const pending = createFlight.isPending || updateFlight.isPending

  const onSubmit = async (event: React.FormEvent) => {
    event.preventDefault()
    setSubmitted(true)
    setSubmitError(null)
    if (Object.keys(validateFlight(values)).length > 0 || submittingRef.current) return
    submittingRef.current = true
    try {
      if (isEdit) {
        await updateFlight.mutateAsync({ id: id!, body: toFlightRequest(values) })
        navigate('/provider/flights')
      } else {
        const created = await createFlight.mutateAsync(toFlightRequest(values))
        // Un vuelo nuevo nace en borrador y sin salidas: el paso siguiente natural es cargarlas.
        navigate(created.id ? `/provider/flights/${created.id}/departures` : '/provider/flights')
      }
    } catch (error) {
      setSubmitError(flightErrorMessage(error))
    } finally {
      submittingRef.current = false
    }
  }

  return (
    <div className="max-w-2xl">
      <Link
        to="/provider/flights"
        className="mb-4 inline-flex items-center gap-1.5 text-sm font-medium text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver a Vuelos
      </Link>
      <PageHeader
        title={isEdit ? 'Editar vuelo' : 'Nuevo vuelo'}
        description="La ruta y las condiciones del vuelo. Las fechas, los asientos y las tarifas se cargan en Salidas."
        actions={<DemoBadge />}
      />

      <form className="flex flex-col gap-6" onSubmit={(e) => void onSubmit(e)} noValidate>
        <Card>
          <CardContent className="grid grid-cols-1 gap-4 pt-6 sm:grid-cols-2">
            <AirportField
              label="Origen"
              role="origin"
              value={values.originIata}
              valueLabel={values.originLabel}
              onChange={(iata, label) => setValues((c) => ({ ...c, originIata: iata, originLabel: label }))}
              error={errors.originIata}
            />
            <AirportField
              label="Destino (Bolivia)"
              role="destination"
              value={values.destinationIata}
              valueLabel={values.destinationLabel}
              onChange={(iata, label) => setValues((c) => ({ ...c, destinationIata: iata, destinationLabel: label }))}
              error={errors.destinationIata}
            />
            <div className="flex flex-col gap-1.5 sm:col-span-2">
              <Label htmlFor="carrier-name">Aerolínea u operador</Label>
              <Input id="carrier-name" value={values.carrierName} maxLength={100} onChange={(e) => set('carrierName', e.target.value)} placeholder="ej. Boliviana de Aviación" />
              {errors.carrierName && <p className="text-xs text-destructive">{errors.carrierName}</p>}
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="carrier-iata">Código de aerolínea (opcional)</Label>
              <Input
                id="carrier-iata"
                value={values.carrierIata}
                maxLength={2}
                onChange={(e) => set('carrierIata', e.target.value.toUpperCase())}
                placeholder="OB"
              />
              {errors.carrierIata && <p className="text-xs text-destructive">{errors.carrierIata}</p>}
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="flight-number">Número de vuelo</Label>
              <Input
                id="flight-number"
                value={values.flightNumber}
                maxLength={5}
                onChange={(e) => set('flightNumber', e.target.value.toUpperCase())}
                placeholder="712"
              />
              {errors.flightNumber && <p className="text-xs text-destructive">{errors.flightNumber}</p>}
            </div>
            <div className="flex flex-col gap-1.5">
              <Label>Cabina</Label>
              <Select value={values.cabinClass} onValueChange={(v) => set('cabinClass', v)}>
                <SelectTrigger aria-label="Cabina">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {CABIN_CLASSES.map((c) => (
                    <SelectItem key={c} value={c}>
                      {cabinLabel[c]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="checked-bags">Valijas despachadas</Label>
                <Input id="checked-bags" type="number" min={0} max={3} value={values.checkedBags} onChange={(e) => set('checkedBags', clamp(e.target.value, 0, 3))} />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="carry-on-bags">Equipaje de mano</Label>
                <Input id="carry-on-bags" type="number" min={0} max={2} value={values.carryOnBags} onChange={(e) => set('carryOnBags', clamp(e.target.value, 0, 2))} />
              </div>
            </div>
            <div className="flex flex-col gap-1.5 sm:col-span-2">
              <Label htmlFor="fare-rules">Condiciones de la tarifa (opcional)</Label>
              <Textarea
                id="fare-rules"
                rows={4}
                maxLength={2000}
                value={values.fareRules}
                onChange={(e) => set('fareRules', e.target.value)}
                placeholder="Cambios de fecha, no-show, equipaje extra…"
              />
              {errors.fareRules && <p className="text-xs text-destructive">{errors.fareRules}</p>}
            </div>
          </CardContent>
        </Card>

        <CancellationPolicySection tiers={values.cancellationPolicy} onChange={(t) => set('cancellationPolicy', t)} />

        {submitError && <Alert variant="destructive">{submitError}</Alert>}
        {isEdit && (
          <p className="text-xs text-muted-foreground">
            Si el vuelo ya tiene reservas, la ruta, la aerolínea, el número, la cabina y el equipaje quedan fijos. Las
            condiciones de la tarifa y la política de cancelación se pueden cambiar, y no afectan lo ya vendido.
          </p>
        )}

        <div className="flex gap-2">
          <Button type="submit" loading={pending}>
            {isEdit ? 'Guardar cambios' : 'Crear vuelo'}
          </Button>
          <Button type="button" variant="outline" disabled={pending} onClick={() => navigate('/provider/flights')}>
            Cancelar
          </Button>
        </div>
      </form>
    </div>
  )
}

function clamp(raw: string, min: number, max: number) {
  const n = Number.parseInt(raw, 10)
  return Number.isNaN(n) ? min : Math.min(max, Math.max(min, n))
}
