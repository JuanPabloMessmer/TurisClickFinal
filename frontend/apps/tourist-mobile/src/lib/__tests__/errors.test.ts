import { AxiosError, AxiosHeaders } from 'axios'
import { safeValidationMessages, toApiError } from '@/lib/errors'

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

describe('toApiError — errores del servidor (5xx)', () => {
  /**
   * Hoy el GlobalExceptionHandler del backend pone el mensaje crudo de la excepción en `detail` de un 500
   * (deuda técnica documentada). La app nunca debe mostrarlo.
   */
  it('nunca muestra el detail de un 500, aunque venga', () => {
    const error = toApiError(
      axiosErrorWith(500, { detail: 'Npgsql.PostgresException (0x80004005): 23505: duplicate key value violates "ix_reservations"' }),
    )

    expect(error.message).not.toContain('Npgsql')
    expect(error.message).not.toContain('ix_reservations')
    expect(error.message).toBe('Tuvimos un problema de nuestro lado. Intentá de nuevo en un momento.')
    expect(error.status).toBe(500)
  })

  it('tampoco expone un código en un 5xx', () => {
    expect(toApiError(axiosErrorWith(503, { detail: 'boom', errorCode: 'SOMETHING' })).code).toBeUndefined()
  })

  it('usa el mensaje genérico de servidor para cualquier 5xx sin cuerpo', () => {
    expect(toApiError(axiosErrorWith(502)).message).toBe('Tuvimos un problema de nuestro lado. Intentá de nuevo en un momento.')
  })

  it('sigue usando el detail de dominio en un 4xx', () => {
    expect(toApiError(axiosErrorWith(410, { detail: 'La reserva expiró y su cupo ya fue liberado.' })).message).toBe(
      'La reserva expiró y su cupo ya fue liberado.',
    )
  })
})

describe('toApiError — validación 400 (ValidationProblemDetails)', () => {
  it('extrae los mensajes de validación seguros en vez de caer en "Algo salió mal"', () => {
    const error = toApiError(
      axiosErrorWith(400, {
        title: 'One or more validation errors occurred.',
        errors: { Travelers: ['The field Travelers must be between 1 and 100.'] },
      }),
    )

    expect(error.message).toBe('The field Travelers must be between 1 and 100.')
  })

  it('incluye el mensaje propio del backend y descarta duplicados', () => {
    const error = toApiError(
      axiosErrorWith(400, {
        errors: {
          ExperienceAvailabilityId: ['Debe indicarse exactamente uno de ExperienceAvailabilityId o PackageAvailabilityId.'],
          PackageAvailabilityId: ['Debe indicarse exactamente uno de ExperienceAvailabilityId o PackageAvailabilityId.'],
        },
      }),
    )

    expect(error.message).toBe('Debe indicarse exactamente uno de ExperienceAvailabilityId o PackageAvailabilityId.')
  })

  it('descarta los mensajes técnicos de conversión de JSON', () => {
    const error = toApiError(
      axiosErrorWith(400, {
        errors: {
          '$.travelers': ['The JSON value could not be converted to System.Int32. Path: $.travelers | LineNumber: 0 | BytePositionInLine: 15.'],
        },
      }),
    )

    expect(error.message).toBe('Revisá los datos e intentá de nuevo.')
    expect(error.message).not.toContain('System.Int32')
  })

  it('un 400 de dominio con detail (ValidationAppException) sigue mostrando ese detail', () => {
    expect(toApiError(axiosErrorWith(400, { detail: 'No hay nada para reservar.' })).message).toBe('No hay nada para reservar.')
  })

  it('un 400 sin detail ni errores usa un texto de validación, no el genérico', () => {
    expect(toApiError(axiosErrorWith(400, {})).message).toBe('Revisá los datos e intentá de nuevo.')
  })
})

describe('safeValidationMessages', () => {
  it('tolera cuerpos inesperados', () => {
    expect(safeValidationMessages(undefined)).toEqual([])
    expect(safeValidationMessages('texto')).toEqual([])
    expect(safeValidationMessages({ campo: 'no es un array' })).toEqual([])
    expect(safeValidationMessages({ campo: [42, null, '  '] })).toEqual([])
  })
})
