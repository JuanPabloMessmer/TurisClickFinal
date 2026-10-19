import { cn } from '@/lib/utils'

/**
 * Marca de TurisClick: una cumbre y su reflejo.
 *
 * El salar reflejando la montaña es la imagen de Bolivia que no necesita decoración — entra por el paisaje,
 * no por el aguayo (DESIGN.md §1, regla 2). Reemplaza al sol y la cumbre anteriores, que eran dos primitivas
 * genéricas: cualquier aplicación de viajes tiene ese mismo dibujo.
 *
 * Dos triángulos y nada más. Se lee a 16px, funciona en una sola tinta y en negativo, y hereda el color de
 * quien lo contiene (`fill-current`), así que la misma pieza sirve sobre el sidebar oscuro y sobre blanco.
 * El reflejo va más angosto, más bajo y al 45%: así es como se ve un reflejo de lejos, y además evita que
 * las dos piezas se fundan en un hexágono. Entre las dos queda un corte de agua limpio.
 */
export function BrandGlyph({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" fill="none" className={className} aria-hidden="true">
      <path d="M16 4 L27 18 L5 18 Z" className="fill-current" />
      <path d="M16 26 L9 21.5 L23 21.5 Z" className="fill-current opacity-45" />
    </svg>
  )
}

export function BrandMark({ subtitle, inverted = true }: { subtitle?: string; inverted?: boolean }) {
  return (
    <div className="flex items-center gap-2.5">
      <BrandGlyph className={cn('h-7 w-7 shrink-0', inverted ? 'text-accent-fill' : 'text-primary')} />
      <div className="leading-tight">
        <p className={cn('text-heading font-semibold tracking-tight', inverted ? 'text-white' : 'text-foreground')}>
          TurisClick
        </p>
        {subtitle && (
          <p className={cn('text-caption font-medium', inverted ? 'text-white/60' : 'text-ink-muted')}>{subtitle}</p>
        )}
      </div>
    </div>
  )
}
