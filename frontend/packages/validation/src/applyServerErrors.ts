import { toApiError } from './httpErrors'

/** Firma mínima de `setError` de react-hook-form (sin depender de la librería). */
export type SetFieldError<TField extends string> = (
  name: TField,
  error: { type: string; message: string },
  options?: { shouldFocus: boolean },
) => void

/**
 * Ubica los errores del servidor debajo de su campo (docs/auditoria-validaciones.md, §9.2).
 *
 * - Cada clave de `errors` que corresponde a un campo del formulario (`fields`, o un alias en `aliases`, p. ej.
 *   `name` ⇒ `companyName`) se marca con `setError` y recibe el foco el primero.
 * - Lo que no tiene campo se devuelve como texto para el aviso general.
 * - Si todos los errores quedaron en sus campos, devuelve `null`: no hace falta repetirlos arriba.
 */
export function applyServerErrors<TField extends string>(
  error: unknown,
  setError: SetFieldError<TField>,
  fields: readonly TField[],
  aliases: Partial<Record<string, TField>> = {},
): string | null {
  const apiError = toApiError(error)
  const leftovers: string[] = []
  let applied = 0

  for (const [key, messages] of Object.entries(apiError.fieldErrors)) {
    const field = (aliases[key] ?? key) as TField
    if (fields.includes(field)) {
      setError(field, { type: 'server', message: messages[0] }, { shouldFocus: applied === 0 })
      applied++
    } else {
      leftovers.push(...messages)
    }
  }

  if (applied === 0) return apiError.message
  return leftovers.length > 0 ? [...new Set(leftovers)].join(' ') : null
}
