import { cn } from '@/lib/utils'

/**
 * Marca de TurisClick. Dos primitivas de paisaje —un sol bajo y una cumbre— sin folclore ni patrones
 * textiles: Bolivia entra por la fotografía y el contenido, no por la decoración (DESIGN.md §1).
 */
export function BrandGlyph({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" fill="none" className={className} aria-hidden="true">
      <circle cx="21" cy="12" r="5" className="fill-current opacity-45" />
      <path d="M3 25 L12 9 L21 25 Z" className="fill-current" />
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
