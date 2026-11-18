/**
 * Traducción de los errores HTTP del backend a mensajes para una persona (docs/auditoria-validaciones.md, §9.2).
 *
 * El backend responde ProblemDetails (RFC 7807): `detail` en español, `errorCode` opcional y, en los 400/409 de
 * validación, `errors: { campo: [mensajes] }` con las claves del formulario. Acá se decide qué mostrar sin dejar
 * pasar nunca un mensaje técnico o en inglés: ni el `detail` de un 5xx, ni el texto de Axios ("Network Error"),
 * ni los mensajes por defecto de ASP.NET que quedaran en algún DTO.
 *
 * No depende de Axios: reconoce sus errores por la forma (`isAxiosError`, `response`, `code`).
 */
export interface ApiError {
  /** Mensaje listo para mostrar en un aviso general. */
  message: string
  status?: number
  /** Ej. DUPLICATE, CATEGORY_IN_USE, VALIDATION_FAILED. Sólo si el backend lo mandó. */
  code?: string
  /** Mensajes por campo (claves del formulario), ya filtrados. */
  fieldErrors: Record<string, string[]>
  /** true si no hubo respuesta del servidor (sin conexión, servidor apagado, tiempo agotado). */
  isNetworkError: boolean
}

export const NETWORK_MESSAGE = 'No pudimos conectarnos con el servidor. Revisá tu conexión e intentá de nuevo.'
export const TIMEOUT_MESSAGE = 'El servidor tardó demasiado en responder. Intentá de nuevo en un momento.'
export const SERVER_MESSAGE = 'Tuvimos un problema de nuestro lado. Intentá de nuevo en un momento.'
export const UNEXPECTED_MESSAGE = 'Ocurrió un error inesperado.'
export const VALIDATION_MESSAGE = 'Hay datos para corregir. Revisá el formulario.'
export const INVALID_VALUE_MESSAGE = 'El valor de este campo no es válido.'

const MESSAGE_BY_STATUS: Record<number, string> = {
  400: VALIDATION_MESSAGE,
  401: 'Tu sesión expiró. Iniciá sesión de nuevo.',
  403: 'No tenés permiso para hacer esto.',
  404: 'No encontramos lo que buscabas.',
  409: 'Esta acción ya no es posible en este momento.',
  410: 'Esto ya no está disponible.',
  413: 'El archivo o los datos enviados son demasiado grandes.',
}

/** Mensajes por defecto de ASP.NET / System.Text.Json / HTTP, todos en inglés o técnicos. */
const ENGLISH_OR_TECHNICAL =
  /^(The |'.*' |A non-empty request body|The JSON value|JSON deserialization|Could not|Unexpected|Request failed|Network Error|timeout of )|\bfield\b|\bmust be\b|\bis required\b|\bis not a valid\b|could not be converted|LineNumber|BytePositionInLine|System\.|\$\.|^(Bad Request|Unauthorized|Forbidden|Not Found|Conflict|Internal Server Error)$/i

export const isSafeMessage = (message: unknown): message is string =>
  typeof message === 'string' && message.trim().length > 0 && !ENGLISH_OR_TECHNICAL.test(message.trim())

/**
 * Error cuyo mensaje ya está escrito para la persona (en español, sin detalles técnicos). Es la única forma de
 * que un `Error` que no viene del servidor muestre su texto: el de cualquier otro `Error` puede ser técnico
 * ("Cannot read properties of undefined") y se reemplaza por un mensaje genérico.
 */
export class UserMessageError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'UserMessageError'
  }
}

interface HttpErrorLike {
  isAxiosError?: boolean
  code?: string
  response?: { status?: number; data?: unknown }
}

interface ProblemDetails {
  title?: unknown
  detail?: unknown
  errorCode?: unknown
  errors?: unknown
}

const isHttpError = (error: unknown): error is HttpErrorLike =>
  typeof error === 'object' && error !== null && (error as HttpErrorLike).isAxiosError === true

/** Errores por campo de un ValidationProblemDetails, sin mensajes técnicos ni repetidos. */
export function fieldErrorsOf(errors: unknown): Record<string, string[]> {
  if (!errors || typeof errors !== 'object') return {}
  const result: Record<string, string[]> = {}
  for (const [key, value] of Object.entries(errors as Record<string, unknown>)) {
    if (!Array.isArray(value)) continue
    const messages = value.map((m) => (isSafeMessage(m) ? m.trim() : INVALID_VALUE_MESSAGE))
    const unique = [...new Set(messages)]
    if (unique.length > 0) result[key] = unique
  }
  return result
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof UserMessageError) return { message: error.message, fieldErrors: {}, isNetworkError: false }
  if (!isHttpError(error)) return { message: UNEXPECTED_MESSAGE, fieldErrors: {}, isNetworkError: false }

  if (!error.response) {
    const timedOut = error.code === 'ECONNABORTED' || error.code === 'ETIMEDOUT'
    return { message: timedOut ? TIMEOUT_MESSAGE : NETWORK_MESSAGE, fieldErrors: {}, isNetworkError: true }
  }

  const status = error.response.status ?? 0
  const problem = (error.response.data ?? {}) as ProblemDetails
  const code = typeof problem.errorCode === 'string' ? problem.errorCode : undefined

  // Un 500 nunca muestra su detalle ni su código: es una falla que nadie previó.
  if (status === 500 || (status > 500 && !isSafeMessage(problem.detail)))
    return { message: SERVER_MESSAGE, status, fieldErrors: {}, isNetworkError: false }

  // 501 y 503 son estados declarados por el backend (funcionalidad todavía no disponible, almacenamiento o
  // procesador de pagos caídos) y traen un mensaje escrito para la persona.
  if (status > 500 && isSafeMessage(problem.detail))
    return { message: problem.detail.trim(), status, code, fieldErrors: {}, isNetworkError: false }

  const fieldErrors = fieldErrorsOf(problem.errors)
  const fieldMessages = Object.values(fieldErrors).flat()
  const detail = isSafeMessage(problem.detail) ? problem.detail.trim() : undefined
  const title = isSafeMessage(problem.title) ? problem.title.trim() : undefined

  const message =
    detail ??
    (fieldMessages.length > 0 ? [...new Set(fieldMessages)].join(' ') : undefined) ??
    MESSAGE_BY_STATUS[status] ??
    title ??
    UNEXPECTED_MESSAGE

  return { message, status, code, fieldErrors, isNetworkError: false }
}

/** Atajo para pantallas que sólo muestran un aviso general. */
export const getErrorMessage = (error: unknown): string => toApiError(error).message
