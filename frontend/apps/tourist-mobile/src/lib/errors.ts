import axios from 'axios'

/**
 * El backend responde errores como ProblemDetails (RFC 7807) y algunos traen además `errorCode` legible
 * por máquina en la raíz del JSON. Acá se traduce todo eso a algo que una pantalla pueda mostrar sin
 * repetir la misma cadena de `?.` en cada componente.
 */
export interface ApiError {
  /** Mensaje ya listo para mostrarle a la persona. Nunca contiene detalles técnicos del servidor. */
  message: string
  status?: number
  /** Ej. REFUND_POLICY_REQUIRED, RESERVATION_NO_LONGER_PAYABLE. Presente solo si el backend lo mandó. */
  code?: string
  /** true si no hubo respuesta del servidor (avión, wifi caído, backend apagado, timeout). */
  isNetworkError: boolean
}

interface ProblemDetails {
  title?: string
  detail?: string
  errorCode?: string
  /** Solo en los 400 de validación de ASP.NET (ValidationProblemDetails): campo → mensajes. */
  errors?: unknown
}

const NETWORK_MESSAGE = 'No pudimos conectarnos. Revisá tu conexión e intentá de nuevo.'
const UNEXPECTED_MESSAGE = 'Algo salió mal. Intentá de nuevo en un momento.'
const SERVER_MESSAGE = 'Tuvimos un problema de nuestro lado. Intentá de nuevo en un momento.'
const VALIDATION_MESSAGE = 'Revisá los datos e intentá de nuevo.'

const MESSAGE_BY_STATUS: Record<number, string> = {
  400: VALIDATION_MESSAGE,
  401: 'Tu sesión expiró. Iniciá sesión de nuevo.',
  403: 'No tenés permiso para hacer esto.',
  404: 'No encontramos lo que buscabas.',
  409: 'Esta acción ya no es posible en este momento.',
  410: 'Esto ya no está disponible.',
}

/**
 * Mensajes que ASP.NET genera sobre la forma del JSON o la conversión de tipos ("The JSON value could not
 * be converted to System.Guid. Path: $.x | LineNumber: 0…"). Son técnicos: nunca se muestran.
 */
const TECHNICAL_VALIDATION_PATTERN = /\$\.|System\.|JSON|LineNumber|BytePositionInLine|could not be converted|Path:/i

/** Extrae de un ValidationProblemDetails solo los mensajes aptos para una persona, sin duplicados. */
export function safeValidationMessages(errors: unknown): string[] {
  if (!errors || typeof errors !== 'object') return []

  const messages = Object.values(errors as Record<string, unknown>)
    .flatMap((value) => (Array.isArray(value) ? value : []))
    .filter((message): message is string => typeof message === 'string')
    .map((message) => message.trim())
    .filter((message) => message.length > 0 && !TECHNICAL_VALIDATION_PATTERN.test(message))

  return [...new Set(messages)]
}

export function toApiError(error: unknown): ApiError {
  if (!axios.isAxiosError(error)) {
    return { message: UNEXPECTED_MESSAGE, isNetworkError: false }
  }

  if (!error.response) {
    return { message: NETWORK_MESSAGE, isNetworkError: true }
  }

  const status = error.response.status
  const problem = error.response.data as ProblemDetails | undefined

  // Un 5xx NUNCA muestra `detail`: hoy el GlobalExceptionHandler del backend pone ahí el mensaje crudo de
  // la excepción (nombres de tablas, stack de EF...). Tampoco se expone un código.
  if (status >= 500) {
    return { message: SERVER_MESSAGE, status, isNetworkError: false }
  }

  // Los 400 de ModelState no traen `detail`, sino un diccionario de errores por campo.
  if (status === 400) {
    const validation = safeValidationMessages(problem?.errors)
    if (validation.length > 0) {
      return { message: validation.join('\n'), status, code: problem?.errorCode, isNetworkError: false }
    }
  }

  // En un 4xx, `detail` es el mensaje de dominio que escribe el backend (ya en español y pensado para el
  // usuario); solo se cae a un texto genérico por status cuando no vino ninguno.
  const message = problem?.detail?.trim() || MESSAGE_BY_STATUS[status] || UNEXPECTED_MESSAGE

  return { message, status, code: problem?.errorCode, isNetworkError: false }
}
