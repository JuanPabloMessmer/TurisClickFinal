import { cva, type VariantProps } from 'class-variance-authority'
import * as React from 'react'
import { cn } from '@/lib/utils'

/**
 * DESIGN.md §12. Patrón "suave": texto 800 sobre fondo 100. Mide 6.37–7.15:1, contra el 4.38 que daba
 * el patrón anterior (token sólido sobre su propio tinte al 10%) en texto de 12px.
 *
 * El color nunca viaja solo: un badge siempre lleva texto, jamás es un punto de color.
 */
const badgeVariants = cva(
  'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-caption font-semibold',
  {
    variants: {
      variant: {
        default: 'bg-primary text-primary-foreground',
        brand: 'bg-primary/10 text-primary',
        neutral: 'bg-muted text-ink-muted',
        success: 'bg-success-soft text-success-soft-foreground',
        warning: 'bg-warning-soft text-warning-soft-foreground',
        destructive: 'bg-destructive-soft text-destructive-soft-foreground',
        info: 'bg-info-soft text-info-soft-foreground',
        outline: 'border border-border-control text-foreground',
      },
    },
    defaultVariants: { variant: 'default' },
  },
)

export interface BadgeProps extends React.HTMLAttributes<HTMLDivElement>, VariantProps<typeof badgeVariants> {}

export function Badge({ className, variant, ...props }: BadgeProps) {
  return <div className={cn(badgeVariants({ variant }), className)} {...props} />
}
