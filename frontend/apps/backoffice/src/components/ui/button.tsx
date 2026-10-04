import { Slot } from '@radix-ui/react-slot'
import { cva, type VariantProps } from 'class-variance-authority'
import * as React from 'react'
import { Spinner } from '@/components/ui/spinner'
import { cn } from '@/lib/utils'

/**
 * DESIGN.md §9. Una sola acción primaria por pantalla; `accent` solo donde el énfasis es dinero o
 * escasez; `destructive` siempre confirma antes. Los hovers neutros usan `muted`, nunca el acento.
 *
 * Alturas: 40 en escritorio y 44 por debajo de `sm`, porque debajo de ese breakpoint la pantalla es
 * táctil y 36px quedaba por debajo del mínimo (DESIGN.md §14).
 */
const buttonVariants = cva(
  'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-sm text-label font-medium transition-colors disabled:pointer-events-none disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background',
  {
    variants: {
      variant: {
        default: 'bg-primary text-primary-foreground hover:bg-secondary',
        secondary: 'bg-secondary text-secondary-foreground hover:bg-primary',
        accent: 'bg-accent-fill text-accent-fill-foreground hover:brightness-95',
        destructive: 'bg-destructive text-destructive-foreground hover:brightness-110',
        outline: 'border border-border-control bg-surface text-primary hover:bg-muted',
        ghost: 'text-ink-muted hover:bg-muted hover:text-foreground',
        link: 'text-primary underline-offset-4 hover:underline',
      },
      size: {
        default: 'h-11 px-4 sm:h-10',
        sm: 'h-11 px-3 sm:h-8 sm:text-caption',
        lg: 'h-12 rounded-md px-6 text-body',
        icon: 'h-11 w-11 sm:h-10 sm:w-10',
      },
    },
    defaultVariants: {
      variant: 'default',
      size: 'default',
    },
  },
)

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean
  /** Muestra el spinner y bloquea el botón: una acción que viaja por red no se puede disparar dos veces. */
  loading?: boolean
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, asChild = false, loading = false, disabled, children, ...props }, ref) => {
    const Comp = asChild ? Slot : 'button'

    // Con `asChild` el hijo es quien renderiza: no se le puede inyectar un spinner sin romper el slot.
    if (asChild) {
      return <Comp className={cn(buttonVariants({ variant, size, className }))} ref={ref} {...props}>{children}</Comp>
    }

    return (
      <button
        className={cn(buttonVariants({ variant, size, className }))}
        ref={ref}
        disabled={disabled || loading}
        aria-busy={loading || undefined}
        {...props}
      >
        {loading && <Spinner className="h-4 w-4 border-current/30 border-t-current" />}
        {children}
      </button>
    )
  },
)
Button.displayName = 'Button'
