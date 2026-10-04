import { cn } from '@/lib/utils'

/**
 * El gesto visual propio del Backoffice (DESIGN.md §1): el trabajo de un operador turístico es
 * ocupación, así que la ocupación es el elemento gráfico que se repite.
 *
 * Barra y número van juntos siempre: la barra sola no se puede leer con precisión y el número solo no
 * se escanea de un vistazo. Y el estado nunca lo comunica el color por su cuenta — cuando importa
 * (completo, últimos lugares) hay una etiqueta de texto al lado.
 */
export function CapacityMeter({
  reserved,
  total,
  className,
  showLabel = true,
}: {
  reserved: number
  total: number
  className?: string
  showLabel?: boolean
}) {
  const safeTotal = Math.max(total, 0)
  const safeReserved = Math.min(Math.max(reserved, 0), safeTotal)
  const percent = safeTotal === 0 ? 0 : Math.round((safeReserved / safeTotal) * 100)
  const free = safeTotal - safeReserved

  const state = free === 0 && safeTotal > 0 ? 'full' : free <= 2 && safeTotal > 0 ? 'low' : 'open'
  const label = state === 'full' ? 'Completo' : state === 'low' ? 'Últimos lugares' : null

  return (
    <div className={cn('flex items-center gap-2.5', className)}>
      <div
        className="h-1 w-16 shrink-0 overflow-hidden rounded-full bg-border"
        role="img"
        aria-label={`${safeReserved} de ${safeTotal} lugares reservados`}
      >
        <div
          className={cn('h-full rounded-full', state === 'full' ? 'bg-accent' : 'bg-secondary')}
          style={{ width: `${percent}%` }}
        />
      </div>
      {showLabel && (
        <span className="tabular text-label text-ink-muted">
          <span className="font-semibold text-foreground">{safeReserved}</span>/{safeTotal}
          {label && <span className="ml-1.5 font-medium text-foreground">· {label}</span>}
        </span>
      )}
    </div>
  )
}
