import { cva, type VariantProps } from 'class-variance-authority'
import * as React from 'react'
import { cn } from '@/lib/utils'

/**
 * Los badges de estado usan el patrón "soft" (fondo al 10% del color + texto en el color sólido) en vez
 * de fondo sólido + texto blanco: mejor contraste con texto pequeño y es el estándar de facto en SaaS
 * moderno (Linear, Stripe, Vercel). "neutral" es para estados inactivos/informativos (no es "secondary":
 * ese nombre queda para cuando realmente se quiere mostrar la marca turquesa).
 */
const badgeVariants = cva(
  'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold transition-colors',
  {
    variants: {
      variant: {
        default: 'border-transparent bg-primary text-primary-foreground',
        secondary: 'border-transparent bg-secondary/10 text-secondary',
        neutral: 'border-transparent bg-muted text-muted-foreground',
        success: 'border-transparent bg-success/10 text-success',
        warning: 'border-transparent bg-warning/10 text-warning',
        destructive: 'border-transparent bg-destructive/10 text-destructive',
        info: 'border-transparent bg-info/10 text-info',
        outline: 'border-border text-foreground',
      },
    },
    defaultVariants: { variant: 'default' },
  },
)

export interface BadgeProps extends React.HTMLAttributes<HTMLDivElement>, VariantProps<typeof badgeVariants> {}

export function Badge({ className, variant, ...props }: BadgeProps) {
  return <div className={cn(badgeVariants({ variant }), className)} {...props} />
}
