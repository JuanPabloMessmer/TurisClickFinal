/** Atributos de accesibilidad del control según su error y su ayuda. */
export function fieldAria(id: string, error?: string, hasHint = false) {
  const describedBy = [error ? `${id}-error` : null, hasHint && !error ? `${id}-hint` : null].filter(Boolean).join(' ')
  return {
    id,
    'aria-invalid': error ? true : undefined,
    'aria-describedby': describedBy || undefined,
  } as const
}
