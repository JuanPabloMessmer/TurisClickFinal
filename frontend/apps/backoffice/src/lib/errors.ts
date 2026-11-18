import { getErrorMessage as describeError } from '@turisclick/validation'

/**
 * Mensaje en español para mostrar en un aviso general, a partir de cualquier error.
 *
 * Delega en el traductor compartido de `@turisclick/validation` (docs/auditoria-validaciones.md, §9.2): usa el
 * `detail` de dominio del backend o los mensajes por campo, y nunca deja pasar texto técnico o en inglés — ni el
 * `detail` de un 5xx, ni "Network Error" de Axios, ni los mensajes por defecto de ASP.NET. Para ubicar los errores
 * debajo de cada campo de un formulario, usar `applyServerErrors` del mismo paquete.
 */
export function getErrorMessage(error: unknown): string {
  return describeError(error)
}
