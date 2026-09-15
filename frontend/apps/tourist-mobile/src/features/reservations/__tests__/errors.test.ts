import { describeCancelFailure, describeCreateFailure, describePayFailure, isReservationNotFound } from '@/features/reservations/errors'
import { httpError, networkError } from '@/test-utils'

describe('describeCreateFailure — POST /api/reservations', () => {
  it('409 (sin código) es falta de cupo, con los viajeros pedidos', () => {
    expect(describeCreateFailure(httpError(409, { detail: 'No hay cupo suficiente…' }), 3)).toMatchObject({
      kind: 'NO_CAPACITY',
      message: expect.stringContaining('3 viajeros'),
    })
  })

  it('410 es una fecha que ya no se puede reservar', () => {
    expect(describeCreateFailure(httpError(410), 1).kind).toBe('SLOT_UNAVAILABLE')
  })

  it('404 es un producto que dejó de estar disponible (despublicado o empresa suspendida)', () => {
    expect(describeCreateFailure(httpError(404), 1).kind).toBe('PRODUCT_UNAVAILABLE')
  })

  it('una caída de red manda a revisar Mis viajes: la reserva pudo haberse creado', () => {
    expect(describeCreateFailure(networkError(), 1)).toMatchObject({ kind: 'NETWORK', message: expect.stringContaining('Mis viajes') })
  })
})

describe('describePayFailure — POST /api/reservations/{id}/pay', () => {
  it('410 CON errorCode RESERVATION_NO_LONGER_PAYABLE es expiración', () => {
    expect(describePayFailure(httpError(410, { errorCode: 'RESERVATION_NO_LONGER_PAYABLE' })).kind).toBe('EXPIRED')
  })

  it('410 SIN errorCode (ExpiresAt vencido antes del job) también es expiración', () => {
    expect(describePayFailure(httpError(410, { detail: 'La reserva expiró; el cupo retenido ya no es válido para pagar.' })).kind).toBe('EXPIRED')
  })

  it('409 sin código es un estado desactualizado que obliga a re-leer', () => {
    expect(describePayFailure(httpError(409, { detail: 'La reserva está en estado CONFIRMED; no admite pago.' })).kind).toBe('STALE')
  })

  it('403 y 404 se muestran igual', () => {
    expect(describePayFailure(httpError(403)).kind).toBe('NOT_FOUND')
    expect(describePayFailure(httpError(404)).kind).toBe('NOT_FOUND')
  })

  it('un 5xx nunca filtra el detail del servidor', () => {
    expect(describePayFailure(httpError(500, { detail: 'NullReferenceException at ReservationService' })).message).not.toContain('Exception')
  })
})

describe('describeCancelFailure — POST /api/reservations/{id}/cancel', () => {
  it('reconoce REFUND_POLICY_REQUIRED', () => {
    expect(describeCancelFailure(httpError(409, { errorCode: 'REFUND_POLICY_REQUIRED' })).kind).toBe('REFUND_POLICY_REQUIRED')
  })

  it('reconoce RESERVATION_NOT_CANCELLABLE como no cancelable', () => {
    expect(describeCancelFailure(httpError(409, { errorCode: 'RESERVATION_NOT_CANCELLABLE' })).kind).toBe('NOT_CANCELLABLE')
  })
})

describe('isReservationNotFound', () => {
  it('no distingue una reserva ajena (403) de una inexistente (404)', () => {
    expect(isReservationNotFound(httpError(403))).toBe(true)
    expect(isReservationNotFound(httpError(404))).toBe(true)
    expect(isReservationNotFound(httpError(500))).toBe(false)
  })
})
