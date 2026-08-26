import { cn } from '@/lib/utils'

/** Spinner inline reutilizable — usado tanto en botones/celdas de carga como en FullScreenSpinner. */
export function Spinner({ className }: { className?: string }) {
  return (
    <div
      role="status"
      aria-label="Cargando"
      className={cn('h-4 w-4 animate-spin rounded-full border-2 border-primary/20 border-t-primary', className)}
    />
  )
}
