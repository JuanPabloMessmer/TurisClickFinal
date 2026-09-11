import axios from 'axios'

/**
 * El backend responde errores como ProblemDetails (RFC 7807) y, desde Oleada 7, algunos traen además
 * `extensions.errorCode` legible por máquina. Acá se traduce todo eso a algo que una pantalla pueda
 * mostrar sin repetir la misma cadena de `?.` en cada componente.
 */
export interface ApiError {
  /** Mensaje ya listo para mostrarle a la persona. */
  message: string
  status?: number
  /** Ej. INSUFFICIENT_CAPACITY, PRODUCT_UNAVAILABLE. Presente solo si el backend lo mandó. */
  code?: string
  /** true si no hubo respuesta del servidor (avión, wifi caído, backend apagado). */
  isNetworkError: boolean
}

interface ProblemDetails {
  title?: string
  detail?: string
  errorCode?: string
}

const NETWORK_MESSAGE = 'No pudimos conectarnos. Revisá tu conexión e intentá de nuevo.'
const UNEXPECTED_MESSAGE = 'Algo salió mal. Intentá de nuevo en un momento.'

const MESSAGE_BY_STATUS: Record<number, string> = {
  401: 'Tu sesión expiró. Iniciá sesión de nuevo.',
  403: 'No tenés permiso para hacer esto.',
  404: 'No encontramos lo que buscabas.',
  409: 'Esta acción ya no es posible en este momento.',
  410: 'Esto ya no está disponible.',
  500: UNEXPECTED_MESSAGE,
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

  // `detail` es el mensaje de dominio que escribe el backend (ya en español y pensado para el usuario);
  // solo se cae a un texto genérico por status cuando no vino ninguno.
  const message = problem?.detail?.trim() || MESSAGE_BY_STATUS[status] || UNEXPECTED_MESSAGE

  return { message, status, code: problem?.errorCode, isNetworkError: false }
}
