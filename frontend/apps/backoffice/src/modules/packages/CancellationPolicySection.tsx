import type { CancellationTierDto } from '@turisclick/api-client'
import { Plus, Trash2 } from 'lucide-react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

/**
 * Política de cancelación del paquete.
 *
 * Lo que el operador define es cuánto devuelve según cuánta anticipación haya: "30 días o más, todo; 15 o
 * más, la mitad; menos, nada". No hay un motor de reglas detrás ni hace falta: son tres o cuatro tramos.
 *
 * Dos cosas que esta sección hace visibles porque son del producto, no de la pantalla:
 *
 * 1. **Sin política, el paquete no se cancela desde la app.** No se asume ni 0% ni 100%: se dice que las
 *    reservas confirmadas van a tener que resolverse escribiéndole al operador.
 * 2. **Editarla no cambia lo ya vendido.** Cada reserva guarda la política que la persona aceptó al comprar.
 */
export function CancellationPolicySection({
  tiers,
  onChange,
}: {
  tiers: CancellationTierDto[]
  onChange: (tiers: CancellationTierDto[]) => void
}) {
  const enabled = tiers.length > 0
  const errors = validate(tiers)

  const setTier = (index: number, patch: Partial<CancellationTierDto>) =>
    onChange(tiers.map((tier, i) => (i === index ? { ...tier, ...patch } : tier)))

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-heading">Política de cancelación</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <label className="flex items-start gap-3">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(event) =>
              onChange(
                event.target.checked
                  ? // Arranca con la política más común, para que nadie tenga que armarla de cero.
                    [
                      { minDaysBefore: 30, refundPercentage: 100 },
                      { minDaysBefore: 15, refundPercentage: 50 },
                      { minDaysBefore: 0, refundPercentage: 0 },
                    ]
                  : [],
              )
            }
            className="mt-1 h-4 w-4 accent-[color:var(--color-primary)]"
          />
          <span>
            <span className="block text-body font-medium text-foreground">
              Los viajeros pueden cancelar y recibir un reembolso
            </span>
            <span className="block text-label text-ink-muted">
              Sin esto, una reserva ya pagada no se puede cancelar desde la app y el viajero tiene que
              escribirte para resolverlo.
            </span>
          </span>
        </label>

        {enabled && (
          <div className="flex flex-col gap-3 border-t border-border pt-4">
            <p className="text-label text-ink-muted">
              Cuanta más anticipación, más se devuelve. Se aplica el primer tramo que alcance la anticipación
              con la que el viajero cancela.
            </p>

            <div className="flex flex-col gap-2">
              {tiers.map((tier, index) => (
                <div key={index} className="flex flex-wrap items-end gap-3 rounded-sm border border-border p-3">
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor={`tier-days-${index}`}>Días antes de la salida</Label>
                    <Input
                      id={`tier-days-${index}`}
                      type="number"
                      min={0}
                      max={365}
                      className="w-28"
                      value={tier.minDaysBefore ?? 0}
                      onChange={(event) => setTier(index, { minDaysBefore: Number(event.target.value) })}
                    />
                  </div>

                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor={`tier-pct-${index}`}>Se devuelve</Label>
                    <div className="flex items-center gap-1.5">
                      <Input
                        id={`tier-pct-${index}`}
                        type="number"
                        min={0}
                        max={100}
                        className="w-24"
                        value={tier.refundPercentage ?? 0}
                        onChange={(event) => setTier(index, { refundPercentage: Number(event.target.value) })}
                      />
                      <span className="text-body text-ink-muted">%</span>
                    </div>
                  </div>

                  <p className="flex-1 pb-2 text-label text-ink-muted">
                    {describe(tier, thresholdAbove(tiers, tier.minDaysBefore ?? 0))}
                  </p>

                  {tiers.length > 1 && (
                    <Button
                      type="button"
                      variant="ghost"
                      aria-label={`Quitar el tramo de ${tier.minDaysBefore} días`}
                      onClick={() => onChange(tiers.filter((_, i) => i !== index))}
                    >
                      <Trash2 className="h-4 w-4" aria-hidden="true" />
                    </Button>
                  )}
                </div>
              ))}
            </div>

            {tiers.length < 6 && (
              <Button
                type="button"
                variant="outline"
                className="self-start"
                onClick={() => onChange([...tiers, { minDaysBefore: 0, refundPercentage: 0 }])}
              >
                <Plus className="h-4 w-4" aria-hidden="true" />
                Agregar un tramo
              </Button>
            )}

            {errors.map((error) => (
              <Alert key={error} variant="warning">
                {error}
              </Alert>
            ))}

            <div className="rounded-sm bg-muted p-3">
              <p className="text-label font-medium text-foreground">Así lo va a ver el viajero</p>
              <ul className="mt-1.5 flex flex-col gap-0.5">
                {[...tiers]
                  .sort((a, b) => (b.minDaysBefore ?? 0) - (a.minDaysBefore ?? 0))
                  .map((tier, index) => (
                    <li key={index} className="text-label text-ink-muted">
                      {describe(tier, thresholdAbove(tiers, tier.minDaysBefore ?? 0))}
                    </li>
                  ))}
              </ul>
              <p className="mt-2 text-caption text-ink-muted">
                Cambiar esta política no afecta a las reservas que ya existen: cada una guarda la que el
                viajero aceptó al comprar.
              </p>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

/**
 * La frase que el viajero lee. Se arma acá para que el preview y la app digan lo mismo.
 *
 * El tramo de 0 días necesita saber dónde empieza el anterior: "menos de los días del tramo anterior" obliga
 * a resolver un acertijo, y "menos de 15 días antes" se entiende de una.
 */
function describe(tier: CancellationTierDto, nextThreshold?: number): string {
  const days = tier.minDaysBefore ?? 0
  const percentage = tier.refundPercentage ?? 0

  const when =
    days > 0
      ? `${days} ${days === 1 ? 'día' : 'días'} o más antes`
      : nextThreshold && nextThreshold > 0
        ? `Menos de ${nextThreshold} ${nextThreshold === 1 ? 'día' : 'días'} antes`
        : 'En cualquier momento'

  if (percentage === 0) return `${when}: no se devuelve nada`
  if (percentage === 100) return `${when}: se devuelve todo`
  return `${when}: se devuelve el ${percentage}%`
}

/** El umbral del tramo inmediatamente anterior, para redactar el tramo de 0 días. */
function thresholdAbove(tiers: CancellationTierDto[], days: number): number | undefined {
  const above = tiers.map((t) => t.minDaysBefore ?? 0).filter((d) => d > days)
  return above.length > 0 ? Math.min(...above) : undefined
}

/**
 * Las mismas reglas que valida el backend. Se repiten acá por trato, no por seguridad: la autoridad sigue
 * siendo el servidor, pero avisar antes de guardar es mejor que rechazar después.
 */
export function validate(tiers: CancellationTierDto[]): string[] {
  if (tiers.length === 0) return []

  const errors: string[] = []
  const ordered = [...tiers].sort((a, b) => (b.minDaysBefore ?? 0) - (a.minDaysBefore ?? 0))

  if (new Set(ordered.map((t) => t.minDaysBefore)).size !== ordered.length)
    errors.push('Hay dos tramos para la misma cantidad de días: cada tramo tiene que empezar en un día distinto.')

  for (let i = 1; i < ordered.length; i++) {
    if ((ordered[i].refundPercentage ?? 0) > (ordered[i - 1].refundPercentage ?? 0)) {
      errors.push('Un tramo más cercano a la salida no puede devolver más que uno más lejano.')
      break
    }
  }

  return errors
}
