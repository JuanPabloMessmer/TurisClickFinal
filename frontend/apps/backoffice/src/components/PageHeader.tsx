import type { ReactNode } from 'react'

/** H1 estándar de toda pantalla del Backoffice — único lugar que define el tamaño/peso del título de página. */
export function PageHeader({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div>
        <h1 className="text-title font-semibold tracking-tight text-foreground">{title}</h1>
        {description && <p className="mt-1 max-w-prose text-body text-ink-muted">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </div>
  )
}
