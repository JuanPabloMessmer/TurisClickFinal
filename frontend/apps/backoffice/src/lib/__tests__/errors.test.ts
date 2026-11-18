import { AxiosError, AxiosHeaders } from 'axios'
import { describe, expect, it } from 'vitest'
import { getErrorMessage } from '@/lib/errors'

const axiosError = (status: number, data: unknown) =>
  new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers: new AxiosHeaders() } as never, {}, {
    status,
    statusText: '',
    data,
    headers: {},
    config: { headers: new AxiosHeaders() } as never,
  })

describe('getErrorMessage', () => {
  it('usa el detalle de dominio del backend (ProblemDetails)', () => {
    expect(getErrorMessage(axiosError(409, { detail: 'Ya existe un destino con ese nombre en el mismo nivel.' }))).toBe(
      'Ya existe un destino con ese nombre en el mismo nivel.',
    )
  })

  it('junta los errores de validación por campo', () => {
    const message = getErrorMessage(axiosError(400, { errors: { name: ['El nombre es obligatorio.'], imageUrl: ['URL inválida.'] } }))
    expect(message).toContain('El nombre es obligatorio.')
    expect(message).toContain('URL inválida.')
  })

  it('no muestra títulos ni mensajes por defecto en inglés', () => {
    expect(getErrorMessage(axiosError(403, { title: 'Forbidden' }))).toBe('No tenés permiso para hacer esto.')
    const validation = getErrorMessage(
      axiosError(400, { title: 'One or more validation errors occurred.', errors: { Name: ['The Name field is required.'] } }),
    )
    expect(validation).not.toMatch(/The |field|required|One or more/)
  })

  it('sin respuesta del servidor avisa en español, no con el texto de Axios', () => {
    const network = new AxiosError('Network Error', 'ERR_NETWORK', { headers: new AxiosHeaders() } as never)
    expect(getErrorMessage(network)).toBe('No pudimos conectarnos con el servidor. Revisá tu conexión e intentá de nuevo.')
  })

  it('cualquier otra cosa no rompe la pantalla ni muestra texto interno', () => {
    expect(getErrorMessage(new Error('boom'))).toBe('Ocurrió un error inesperado.')
    expect(getErrorMessage('???')).toBe('Ocurrió un error inesperado.')
  })
})
