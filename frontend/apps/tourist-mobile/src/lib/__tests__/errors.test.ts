import { AxiosError, AxiosHeaders } from 'axios'
import { toApiError } from '@/lib/errors'

function axiosErrorWith(status: number, data?: unknown) {
  const config = { headers: new AxiosHeaders() }
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', config as never, {}, {
    status,
    statusText: '',
    data,
    headers: {},
    config: config as never,
  })
}

describe('toApiError', () => {
  it('prefiere el detail del ProblemDetails, que es el mensaje que escribió el backend', () => {
    const error = toApiError(axiosErrorWith(409, { detail: 'La disponibilidad ya no tiene cupo.' }))

    expect(error.message).toBe('La disponibilidad ya no tiene cupo.')
    expect(error.status).toBe(409)
    expect(error.isNetworkError).toBe(false)
  })

  it('expone errorCode cuando el backend lo manda', () => {
    const error = toApiError(axiosErrorWith(409, { detail: 'Sin cupo', errorCode: 'INSUFFICIENT_CAPACITY' }))

    expect(error.code).toBe('INSUFFICIENT_CAPACITY')
  })

  it('cae a un texto por status cuando no vino detail', () => {
    expect(toApiError(axiosErrorWith(404, {})).message).toBe('No encontramos lo que buscabas.')
    expect(toApiError(axiosErrorWith(410, {})).message).toBe('Esto ya no está disponible.')
  })

  it('ignora un detail vacío o de solo espacios', () => {
    expect(toApiError(axiosErrorWith(403, { detail: '   ' })).message).toBe('No tenés permiso para hacer esto.')
  })

  it('distingue el fallo de red, que no trae response', () => {
    const networkError = new AxiosError('Network Error', 'ERR_NETWORK')

    const error = toApiError(networkError)

    expect(error.isNetworkError).toBe(true)
    expect(error.status).toBeUndefined()
    expect(error.message).toContain('Revisá tu conexión')
  })

  it('no se rompe con algo que no es un error de axios', () => {
    const error = toApiError(new Error('boom'))

    expect(error.isNetworkError).toBe(false)
    expect(error.message).toBe('Algo salió mal. Intentá de nuevo en un momento.')
  })

  it('usa el genérico para un status que no está mapeado', () => {
    expect(toApiError(axiosErrorWith(418, {})).message).toBe('Algo salió mal. Intentá de nuevo en un momento.')
  })
})
