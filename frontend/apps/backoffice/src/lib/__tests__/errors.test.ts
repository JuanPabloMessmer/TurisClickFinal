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
    const message = getErrorMessage(axiosError(400, { errors: { Name: ['El nombre es obligatorio.'], ImageUrl: ['URL inválida.'] } }))
    expect(message).toContain('El nombre es obligatorio.')
    expect(message).toContain('URL inválida.')
  })

  it('cae al title cuando no hay detalle', () => {
    expect(getErrorMessage(axiosError(403, { title: 'Forbidden' }))).toBe('Forbidden')
  })

  it('sin respuesta del servidor devuelve el mensaje del error de red', () => {
    const network = new AxiosError('Network Error', 'ERR_NETWORK', { headers: new AxiosHeaders() } as never)
    expect(getErrorMessage(network)).toBe('Network Error')
  })

  it('cualquier otra cosa no rompe la pantalla', () => {
    expect(getErrorMessage(new Error('boom'))).toBe('boom')
    expect(getErrorMessage('???')).toBe('Ocurrió un error inesperado.')
  })
})
