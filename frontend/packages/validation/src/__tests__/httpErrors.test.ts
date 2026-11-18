import { describe, expect, it, vi } from 'vitest'
import { applyServerErrors } from '../applyServerErrors'
import { NETWORK_MESSAGE, SERVER_MESSAGE, TIMEOUT_MESSAGE, UNEXPECTED_MESSAGE, UserMessageError, toApiError } from '../httpErrors'

const httpError = (status: number, data: unknown) => ({ isAxiosError: true, response: { status, data } })

describe('toApiError', () => {
  it('usa el detalle de dominio del backend', () => {
    expect(toApiError(httpError(409, { detail: 'Ya existe una empresa con ese NIT.' })).message).toBe(
      'Ya existe una empresa con ese NIT.',
    )
  })

  it('devuelve los errores por campo y los junta para el aviso general', () => {
    const error = toApiError(
      httpError(400, { title: 'Hay datos para corregir.', errors: { legalDocument: ['El NIT sólo puede tener números.'] } }),
    )
    expect(error.fieldErrors).toEqual({ legalDocument: ['El NIT sólo puede tener números.'] })
    expect(error.message).toBe('El NIT sólo puede tener números.')
  })

  it('nunca muestra mensajes por defecto en inglés ni técnicos', () => {
    const error = toApiError(
      httpError(400, {
        title: 'One or more validation errors occurred.',
        errors: {
          companyName: ['The CompanyName field is required.'],
          destinationId: ['The JSON value could not be converted to System.Guid. Path: $.destinationId'],
        },
      }),
    )
    const text = [error.message, ...Object.values(error.fieldErrors).flat()].join(' ')
    expect(text).not.toMatch(/The |System\.|\$\./)
    expect(error.fieldErrors.companyName).toEqual(['El valor de este campo no es válido.'])
  })

  it('un 500 nunca muestra su detalle', () => {
    expect(toApiError(httpError(500, { detail: '42703: column does not exist' })).message).toBe(SERVER_MESSAGE)
  })

  it('un 503 declarado por el backend muestra su mensaje para la persona', () => {
    expect(toApiError(httpError(503, { detail: 'No se pudo guardar la imagen en este momento.' })).message).toBe(
      'No se pudo guardar la imagen en este momento.',
    )
    expect(toApiError(httpError(502, { title: 'Bad Gateway' })).message).toBe(SERVER_MESSAGE)
  })

  it('un título en inglés se reemplaza por un mensaje por estado', () => {
    expect(toApiError(httpError(403, { title: 'Forbidden' })).message).toBe('No tenés permiso para hacer esto.')
  })

  it('sin respuesta del servidor: conexión o tiempo agotado, en español', () => {
    expect(toApiError({ isAxiosError: true, code: 'ERR_NETWORK', message: 'Network Error' }).message).toBe(NETWORK_MESSAGE)
    expect(toApiError({ isAxiosError: true, code: 'ECONNABORTED', message: 'timeout of 60000ms exceeded' }).message).toBe(
      TIMEOUT_MESSAGE,
    )
  })

  it('un error escrito para la persona muestra su mensaje', () => {
    expect(toApiError(new UserMessageError('El servidor no devolvió la dirección de la imagen.')).message).toBe(
      'El servidor no devolvió la dirección de la imagen.',
    )
  })

  it('cualquier otra cosa no rompe la pantalla ni muestra texto interno', () => {
    expect(toApiError(new Error('Cannot read properties of undefined')).message).toBe(UNEXPECTED_MESSAGE)
    expect(toApiError('???').message).toBe(UNEXPECTED_MESSAGE)
  })
})

describe('applyServerErrors', () => {
  it('pone cada error debajo de su campo y no repite nada en el aviso general', () => {
    const setError = vi.fn()
    const banner = applyServerErrors(
      httpError(409, { detail: 'Ya existe una empresa con ese NIT.', errors: { legalDocument: ['Ya existe una empresa con ese NIT.'] } }),
      setError,
      ['companyName', 'legalDocument'] as const,
    )
    expect(setError).toHaveBeenCalledWith(
      'legalDocument',
      { type: 'server', message: 'Ya existe una empresa con ese NIT.' },
      { shouldFocus: true },
    )
    expect(banner).toBeNull()
  })

  it('usa alias cuando el backend nombra distinto el campo', () => {
    const setError = vi.fn()
    applyServerErrors(httpError(409, { errors: { name: ['Ya existe una empresa con ese nombre.'] } }), setError, ['companyName'] as const, {
      name: 'companyName',
    })
    expect(setError).toHaveBeenCalledWith('companyName', expect.objectContaining({ message: 'Ya existe una empresa con ese nombre.' }), expect.anything())
  })

  it('lo que no tiene campo va al aviso general', () => {
    const setError = vi.fn()
    expect(applyServerErrors(httpError(409, { detail: 'Solo se puede editar una empresa aprobada.' }), setError, ['name'] as const)).toBe(
      'Solo se puede editar una empresa aprobada.',
    )
    expect(setError).not.toHaveBeenCalled()
  })
})
