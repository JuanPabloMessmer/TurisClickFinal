import {
  EXPIRY_POLL_INTERVAL_MS,
  canCancel,
  canPay,
  displayStatus,
  formatCountdown,
  isCancelledByOperator,
  itemStatusLabel,
  nextExpiryPoll,
  priceChangeLines,
  projectedTotals,
  reservationTitle,
  secondsLeft,
} from '@/features/reservations/model'
import { itemFixture, reservationFixture } from '@/test-utils'

const NOW = new Date('2026-10-01T12:00:00Z').getTime()
const future = new Date(NOW + 10 * 60_000).toISOString()
const past = new Date(NOW - 1_000).toISOString()

describe('displayStatus', () => {
  it('PENDING_PAYMENT con tiempo: pendiente de pago', () => {
    expect(displayStatus(reservationFixture({ expiresAt: future }), NOW)).toMatchObject({ kind: 'PENDING', label: 'Pendiente de pago' })
  })

  it('PENDING_PAYMENT vencida pero todavía no expirada por el backend: no se presenta como pagable', () => {
    expect(displayStatus(reservationFixture({ expiresAt: past }), NOW).kind).toBe('PENDING_TIME_UP')
  })

  it('CONFIRMED, EXPIRED y CANCELLED se leen tal cual', () => {
    expect(displayStatus(reservationFixture({ status: 'CONFIRMED', items: [itemFixture({ status: 'CONFIRMED' })] }), NOW).label).toBe('Confirmada')
    expect(displayStatus(reservationFixture({ status: 'EXPIRED' }), NOW).label).toBe('Expirada')
    expect(displayStatus(reservationFixture({ status: 'CANCELLED' }), NOW).label).toBe('Cancelada')
  })

  it('PAYMENT_FAILED u otro valor desconocido usa un fallback neutral, sin inventar significado', () => {
    expect(displayStatus(reservationFixture({ status: 'PAYMENT_FAILED' }), NOW)).toMatchObject({ kind: 'UNKNOWN', tone: 'neutral' })
    expect(displayStatus(reservationFixture({ status: 'ALGO_NUEVO' }), NOW).kind).toBe('UNKNOWN')
  })

  it('CONFIRMED con su única línea cancelada por el operador: cancelada por el operador', () => {
    const reservation = reservationFixture({
      status: 'CONFIRMED',
      items: [itemFixture({ status: 'CANCELLED', cancellationReason: 'Cierre del parque por mal clima' })],
    })

    expect(displayStatus(reservation, NOW)).toMatchObject({ kind: 'CANCELLED_BY_OPERATOR', label: 'Cancelada por el operador' })
  })

  it('CONFIRMED multi-ítem con una línea cancelada por el operador: confirmada con aviso', () => {
    const reservation = reservationFixture({
      status: 'CONFIRMED',
      items: [
        itemFixture({ id: 'a', status: 'CONFIRMED' }),
        itemFixture({ id: 'b', status: 'CANCELLED', cancellationReason: 'Vehículo averiado' }),
      ],
    })

    expect(displayStatus(reservation, NOW)).toMatchObject({
      kind: 'CONFIRMED_WITH_OPERATOR_CANCELLATIONS',
      label: 'Confirmada · 1 servicio cancelado por el operador',
    })
  })
})

describe('isCancelledByOperator / itemStatusLabel', () => {
  it('una línea que canceló el turista (reserva CANCELLED, sin motivo) no se atribuye al operador', () => {
    expect(isCancelledByOperator(itemFixture({ status: 'CANCELLED' }), 'CANCELLED')).toBe(false)
  })

  it('una línea con motivo es del operador aunque la reserva luego haya expirado', () => {
    expect(isCancelledByOperator(itemFixture({ status: 'CANCELLED', cancellationReason: 'Paro' }), 'EXPIRED')).toBe(true)
  })

  it('no repite el estado de la línea cuando coincide con el de la reserva', () => {
    expect(itemStatusLabel(itemFixture({ status: 'CONFIRMED' }), 'CONFIRMED')).toBeNull()
    expect(itemStatusLabel(itemFixture({ status: 'CANCELLED', cancellationReason: 'Paro' }), 'CONFIRMED')).toBe('Cancelado por el operador')
  })
})

describe('reservationTitle', () => {
  it('multi-ítem: primer producto y cuántos más', () => {
    const reservation = reservationFixture({
      items: [itemFixture({ experienceTitle: 'Tour Illimani' }), itemFixture({ id: 'b', experienceTitle: null, packageTitle: 'Uyuni 3 días' })],
    })

    expect(reservationTitle(reservation)).toEqual({ title: 'Tour Illimani', extraCount: 1 })
  })
})

describe('canPay / canCancel', () => {
  it('solo se paga PENDING_PAYMENT con tiempo', () => {
    expect(canPay(reservationFixture({ expiresAt: future }), NOW)).toBe(true)
    expect(canPay(reservationFixture({ expiresAt: past }), NOW)).toBe(false)
    expect(canPay(reservationFixture({ status: 'CONFIRMED', expiresAt: future }), NOW)).toBe(false)
  })

  it('una reserva CONFIRMED nunca expone cancelar (no hay política de reembolso)', () => {
    expect(canCancel(reservationFixture({ status: 'CONFIRMED' }))).toBe(false)
    expect(canCancel(reservationFixture({ status: 'EXPIRED' }))).toBe(false)
    expect(canCancel(reservationFixture({ status: 'CANCELLED' }))).toBe(false)
    expect(canCancel(reservationFixture({ status: 'PENDING_PAYMENT' }))).toBe(true)
  })
})

describe('cuenta regresiva', () => {
  it('redondea hacia arriba y nunca es negativa', () => {
    expect(secondsLeft(new Date(NOW + 1_500).toISOString(), NOW)).toBe(2)
    expect(secondsLeft(past, NOW)).toBe(0)
  })

  it('formatea mm:ss y h:mm:ss', () => {
    expect(formatCountdown(0)).toBe('00:00')
    expect(formatCountdown(29 * 60 + 5)).toBe('29:05')
    expect(formatCountdown(3723)).toBe('1:02:03')
  })
})

describe('cambio de precio', () => {
  const response = reservationFixture({
    requiresPriceAcceptance: true,
    items: [
      itemFixture({ id: 'a', travelers: 2, unitPrice: 40, currency: 'USD', priceChanged: true, currentUnitPrice: 60, currentCurrency: 'USD' }),
      itemFixture({ id: 'b', travelers: 1, unitPrice: 300, currency: 'BOB', priceChanged: true, currentUnitPrice: 50, currentCurrency: 'USD' }),
      itemFixture({ id: 'c', travelers: 3, unitPrice: 100, currency: 'BOB', priceChanged: false }),
    ],
  })

  it('lista solo las líneas que cambiaron, con precio y moneda anteriores y actuales', () => {
    expect(priceChangeLines(response)).toEqual([
      expect.objectContaining({ itemId: 'a', previousUnitPrice: 40, previousCurrency: 'USD', currentUnitPrice: 60, currentCurrency: 'USD' }),
      expect.objectContaining({ itemId: 'b', previousUnitPrice: 300, previousCurrency: 'BOB', currentUnitPrice: 50, currentCurrency: 'USD' }),
    ])
  })

  it('proyecta los nuevos totales con currentUnitPrice × travelers agrupados por moneda, sin sumar monedas', () => {
    expect(projectedTotals(response)).toEqual([
      { currency: 'USD', amount: 170 },
      { currency: 'BOB', amount: 300 },
    ])
  })
})

describe('nextExpiryPoll', () => {
  const waiting = reservationFixture({ expiresAt: past })

  it('no hace polling mientras quede tiempo', () => {
    expect(nextExpiryPoll({ data: reservationFixture({ expiresAt: future }), dataUpdatedAt: 1 }, NOW, { polls: 0 }).delay).toBe(false)
  })

  it('re-lee cada 30s como máximo 4 veces después de la primera lectura vencida', () => {
    let tracker = { polls: 0 } as { polls: number; lastSeenUpdatedAt?: number }
    const delays: (number | false)[] = []

    for (let updatedAt = 1; updatedAt <= 6; updatedAt++) {
      const decision = nextExpiryPoll({ data: waiting, dataUpdatedAt: updatedAt }, NOW, tracker)
      tracker = decision.tracker
      delays.push(decision.delay)
    }

    expect(delays).toEqual([EXPIRY_POLL_INTERVAL_MS, EXPIRY_POLL_INTERVAL_MS, EXPIRY_POLL_INTERVAL_MS, EXPIRY_POLL_INTERVAL_MS, false, false])
  })

  it('no gasta intentos cuando TanStack re-evalúa sin una lectura nueva (re-render de la cuenta regresiva)', () => {
    let tracker = nextExpiryPoll({ data: waiting, dataUpdatedAt: 1 }, NOW, { polls: 0 }).tracker
    for (let i = 0; i < 50; i++) tracker = nextExpiryPoll({ data: waiting, dataUpdatedAt: 1 }, NOW, tracker).tracker

    expect(tracker.polls).toBe(1)
  })

  it('se detiene apenas el backend cambia el estado', () => {
    const decision = nextExpiryPoll({ data: reservationFixture({ status: 'EXPIRED', expiresAt: past }), dataUpdatedAt: 9 }, NOW, { polls: 3, lastSeenUpdatedAt: 8 })

    expect(decision).toEqual({ delay: false, tracker: { polls: 0 } })
  })
})
