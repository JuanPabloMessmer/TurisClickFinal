import type { LucideIcon } from 'lucide-react'
import { Inbox } from 'lucide-react'
import { cn } from '@/lib/utils'

/** Estado vacío consistente para tablas/listas — ícono + título + descripción opcional. */
export function EmptyState({
  icon: Icon = Inbox,
  title,
  description,
  className,
}: {
  icon?: LucideIcon
  title: string
  description?: string
  className?: string
}) {
  return (
    <div className={cn('flex flex-col items-center justify-center gap-2 py-10 text-center', className)}>
      <span className="flex h-10 w-10 items-center justify-center rounded-full bg-muted">
        <Icon className="h-5 w-5 text-ink-muted" aria-hidden="true" />
      </span>
      <p className="text-heading font-semibold text-foreground">{title}</p>
      {description && <p className="max-w-sm text-body text-ink-muted">{description}</p>}
    </div>
  )
}
