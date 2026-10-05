import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { flightsApi } from '@turisclick/api-client'
import { Plane, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Spinner } from '@/components/ui/spinner'
import { getErrorMessage } from '@/lib/errors'
import { httpClient } from '@/lib/httpClient'
import { cn } from '@/lib/utils'

/**
 * Configuración del vuelo de un paquete.
 *
 * Lo que el operador define son REGLAS, no un vuelo: desde qué aeropuertos acepta salir, a cuál vuela y
 * cómo se relacionan las fechas del vuelo con las del viaje. La aerolínea, el número de vuelo y la
 * tarifa salen del proveedor cada vez que alguien cotiza — por eso no hay un campo para escribirlos.
 *
 * Vive en su propio bloque con su propio guardado porque la regla cuelga de un paquete que ya existe:
 * hasta que el paquete no está creado no hay dónde colgarla.
 */
export function PackageFlightSection({ packageId }: { packageId?: string }) {
  if (!packageId) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-heading">
            <Plane className="h-4 w-4 text-ink-muted" aria-hidden="true" />
            Vuelo
          </CardTitle>
        </CardHeader>
        <CardContent>
          <p className="text-body text-ink-muted">
            Guardá el paquete y vas a poder configurar acá si incluye vuelo y desde qué ciudades sale.
          </p>
        </CardContent>
      </Card>
    )
  }

  return <FlightRuleEditor packageId={packageId} />
}

const CABINS = [
  { value: 'ECONOMY', label: 'Económica' },
  { value: 'PREMIUM_ECONOMY', label: 'Económica premium' },
  { value: 'BUSINESS', label: 'Ejecutiva' },
  { value: 'FIRST', label: 'Primera' },
]

function FlightRuleEditor({ packageId }: { packageId: string }) {
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const rule = useQuery({
    queryKey: ['packageFlightRule', packageId],
    queryFn: () => flightsApi.getPackageFlightRule(httpClient, packageId),
  })

  const airports = useQuery({
    queryKey: ['airports'],
    queryFn: () => flightsApi.listAirports(httpClient),
    staleTime: 60 * 60_000,
  })

  const [enabled, setEnabled] = useState(false)
  const [destination, setDestination] = useState('')
  const [origins, setOrigins] = useState<string[]>([])
  const [cabin, setCabin] = useState('ECONOMY')
  const [roundTrip, setRoundTrip] = useState(true)
  const [outboundOffset, setOutboundOffset] = useState(0)
  const [inboundOffset, setInboundOffset] = useState(0)

  // Se sincroniza una vez cuando llega la regla guardada; después manda lo que el operador editó.
  useEffect(() => {
    if (!rule.data) return
    setEnabled(true)
    setDestination(rule.data.destinationIata ?? '')
    setOrigins((rule.data.allowedOrigins ?? []).map((origin) => origin.iata ?? '').filter(Boolean))
    setCabin(rule.data.cabinClass ?? 'ECONOMY')
    setRoundTrip(rule.data.roundTrip ?? true)
    setOutboundOffset(rule.data.outboundOffsetDays ?? 0)
    setInboundOffset(rule.data.inboundOffsetDays ?? 0)
  }, [rule.data])

  const save = useMutation({
    mutationFn: () =>
      flightsApi.setPackageFlightRule(httpClient, packageId, {
        destinationIata: destination,
        allowedOriginIatas: origins,
        cabinClass: cabin,
        outboundOffsetDays: outboundOffset,
        inboundOffsetDays: inboundOffset,
        roundTrip,
      }),
    onSuccess: () => {
      setSaved(true)
      setError(null)
      void queryClient.invalidateQueries({ queryKey: ['packageFlightRule', packageId] })
      void queryClient.invalidateQueries({ queryKey: ['myPackages'] })
    },
    onError: (mutationError) => setError(getErrorMessage(mutationError)),
  })

  const remove = useMutation({
    mutationFn: () => flightsApi.removePackageFlightRule(httpClient, packageId),
    onSuccess: () => {
      setEnabled(false)
      setDestination('')
      setOrigins([])
      setSaved(false)
      void queryClient.invalidateQueries({ queryKey: ['packageFlightRule', packageId] })
    },
    onError: (mutationError) => setError(getErrorMessage(mutationError)),
  })

  const toggleOrigin = (iata: string) =>
    setOrigins((current) => (current.includes(iata) ? current.filter((o) => o !== iata) : [...current, iata]))

  const canSave = destination.length === 3 && origins.length > 0 && !origins.includes(destination)

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-heading">
          <Plane className="h-4 w-4 text-ink-muted" aria-hidden="true" />
          Vuelo
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {rule.isPending ? (
          <div className="flex justify-center py-6">
            <Spinner />
          </div>
        ) : (
          <>
            <label className="flex items-start gap-3">
              <input
                type="checkbox"
                checked={enabled}
                onChange={(event) => setEnabled(event.target.checked)}
                className="mt-1 h-4 w-4 accent-[color:var(--color-primary)]"
              />
              <span>
                <span className="block text-body font-medium text-foreground">Este paquete incluye vuelo</span>
                <span className="block text-label text-ink-muted">
                  No cargues aerolínea ni tarifa: el pasaje se busca y se cotiza en el momento, con precios
                  reales de la fecha que elija cada viajero.
                </span>
              </span>
            </label>

            {enabled && (
              <div className="flex flex-col gap-4 border-t border-border pt-4">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="flight-destination">Aeropuerto de destino</Label>
                  <Select value={destination || undefined} onValueChange={(value) => value && setDestination(value)}>
                    <SelectTrigger id="flight-destination">
                      <SelectValue placeholder="¿A qué aeropuerto llega el viaje?" />
                    </SelectTrigger>
                    <SelectContent>
                      {(airports.data ?? []).map((airport) => (
                        <SelectItem key={airport.iata} value={airport.iata!}>
                          {airport.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                <fieldset className="flex flex-col gap-2">
                  <legend className="text-label font-medium text-foreground">Desde qué ciudades sale</legend>
                  <p className="text-label text-ink-muted">
                    El viajero elige una de estas al cotizar. Marcá todas las que puedas operar.
                  </p>
                  <div className="flex flex-wrap gap-2 pt-1">
                    {(airports.data ?? [])
                      .filter((airport) => airport.iata !== destination)
                      .map((airport) => {
                        const active = origins.includes(airport.iata!)
                        return (
                          <button
                            key={airport.iata}
                            type="button"
                            aria-pressed={active}
                            onClick={() => toggleOrigin(airport.iata!)}
                            className={cn(
                              'h-9 rounded-sm border px-3 text-label transition-colors',
                              active
                                ? 'border-primary bg-primary/10 font-medium text-primary'
                                : 'border-border-control text-ink-muted hover:bg-muted',
                            )}
                          >
                            {airport.label}
                          </button>
                        )
                      })}
                  </div>
                </fieldset>

                <div className="grid gap-4 sm:grid-cols-2">
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="flight-cabin">Clase</Label>
                    <Select value={cabin} onValueChange={(value) => value && setCabin(value)}>
                      <SelectTrigger id="flight-cabin">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {CABINS.map((option) => (
                          <SelectItem key={option.value} value={option.value}>
                            {option.label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>

                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="flight-roundtrip">Tipo de viaje</Label>
                    <Select
                      value={roundTrip ? 'ROUND' : 'ONEWAY'}
                      onValueChange={(value) => value && setRoundTrip(value === 'ROUND')}
                    >
                      <SelectTrigger id="flight-roundtrip">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="ROUND">Ida y vuelta</SelectItem>
                        <SelectItem value="ONEWAY">Sólo ida</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="flight-outbound">Vuelo de ida</Label>
                    <Input
                      id="flight-outbound"
                      type="number"
                      min={-7}
                      max={7}
                      value={outboundOffset}
                      onChange={(event) => setOutboundOffset(Number(event.target.value))}
                    />
                    <p className="text-caption text-ink-muted">
                      Días respecto del inicio del paquete. 0 = el mismo día; −1 = la noche anterior.
                    </p>
                  </div>

                  {roundTrip && (
                    <div className="flex flex-col gap-1.5">
                      <Label htmlFor="flight-inbound">Vuelo de vuelta</Label>
                      <Input
                        id="flight-inbound"
                        type="number"
                        min={-7}
                        max={7}
                        value={inboundOffset}
                        onChange={(event) => setInboundOffset(Number(event.target.value))}
                      />
                      <p className="text-caption text-ink-muted">
                        Días respecto del último día del paquete. 0 = el mismo día que termina.
                      </p>
                    </div>
                  )}
                </div>

                {origins.includes(destination) && destination && (
                  <Alert variant="warning">El destino no puede estar también entre las ciudades de salida.</Alert>
                )}

                {error && <Alert variant="destructive">{error}</Alert>}
                {saved && !save.isPending && <Alert variant="success">Regla de vuelo guardada.</Alert>}

                <div className="flex flex-wrap gap-2">
                  <Button type="button" onClick={() => save.mutate()} loading={save.isPending} disabled={!canSave}>
                    Guardar configuración de vuelo
                  </Button>
                  {rule.data && (
                    <Button type="button" variant="ghost" onClick={() => remove.mutate()} loading={remove.isPending}>
                      <Trash2 className="h-4 w-4" aria-hidden="true" />
                      Quitar el vuelo del paquete
                    </Button>
                  )}
                </div>
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}
