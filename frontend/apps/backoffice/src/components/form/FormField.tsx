import type { ReactNode } from 'react'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

/**
 * Etiqueta + control + error, siempre juntos (docs/auditoria-validaciones.md, §9.2).
 *
 * Existe porque antes cada pantalla decidía qué errores mostrar, y en el alta de operador y en Mi empresa los de
 * descripción y teléfono no se mostraban: el envío se bloqueaba sin explicación. Acá el error del campo se
 * muestra siempre, debajo del control, con el texto en español que dio la regla (o el servidor).
 *
 * El control recibe `aria-invalid` y `aria-describedby` con `fieldAria` (./fieldAria).
 */
export function FormField({
  id,
  label,
  error,
  hint,
  counter,
  className,
  children,
}: {
  id: string
  label: ReactNode
  error?: string
  hint?: ReactNode
  /** Contador de caracteres: { length, max }. */
  counter?: { length: number; max: number }
  className?: string
  children: ReactNode
}) {
  const over = counter && counter.length > counter.max
  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <Label htmlFor={id}>{label}</Label>
      {children}
      {hint && !error && (
        <p id={`${id}-hint`} className="text-caption text-ink-muted">
          {hint}
        </p>
      )}
      {(error || counter) && (
        <div className="flex items-start justify-between gap-2">
          {error ? (
            <p id={`${id}-error`} role="alert" className="text-xs text-destructive">
              {error}
            </p>
          ) : (
            <span />
          )}
          {counter && (
            <span className={cn('shrink-0 text-xs tabular-nums', over ? 'text-destructive' : 'text-ink-muted')}>
              {counter.length}/{counter.max}
            </span>
          )}
        </div>
      )}
    </div>
  )
}
