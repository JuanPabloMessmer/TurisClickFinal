#!/usr/bin/env node
/**
 * E2E de vuelos de operador contra una API REAL corriendo (Workstream D).
 *
 * No usa mocks ni toca la base: todo pasa por HTTP, igual que la app y el Backoffice. Pensado para correr
 * contra una API LOCAL con una base AISLADA y el proveedor aéreo simulado (Flights:Provider=Fake). Nunca
 * contra Azure: el script se niega si la URL no es localhost.
 *
 *   E2E_API=http://localhost:5288 E2E_ADMIN_PASSWORD=<contraseña del admin sembrado> node tools/provider-flight-e2e/run.mjs
 *
 * Los pasajeros son sintéticos. Ningún pasaje es real: el entorno es demo y lo dice.
 */

const API = process.env.E2E_API ?? 'http://localhost:5288'
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@turisclick.dev'
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD

if (!/^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(API)) {
  console.error(`Sólo contra una API local; recibí ${API}.`)
  process.exit(2)
}
if (!ADMIN_PASSWORD) {
  console.error('Falta E2E_ADMIN_PASSWORD (la contraseña del admin sembrado en la base aislada).')
  process.exit(2)
}

// ---------------------------------------------------------------- utilidades

const results = []
let current = ''

function check(name, condition, detail = '') {
  results.push({ section: current, name, ok: Boolean(condition), detail })
  console.log(`${condition ? '  PASS' : '  FAIL'}  ${name}${!condition && detail ? ` — ${detail}` : ''}`)
}

function section(title) {
  current = title
  console.log(`\n== ${title}`)
}

async function call(method, path, { token, body } = {}) {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: {
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  const text = await response.text()
  let data = null
  try {
    data = text ? JSON.parse(text) : null
  } catch {
    data = text
  }
  return { status: response.status, data }
}

const unique = () => Math.random().toString(36).slice(2, 10)
const isoDate = (date) => date.toISOString().slice(0, 10)
const daysFromNow = (days) => {
  const date = new Date()
  date.setUTCHours(0, 0, 0, 0)
  date.setUTCDate(date.getUTCDate() + days)
  return date
}
const localDateTime = (date, hour, minutes = 0) =>
  `${isoDate(date)}T${String(hour).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:00`

async function login(email, password) {
  const r = await call('POST', '/api/auth/login', { body: { email, password } })
  if (r.status !== 200) throw new Error(`login ${email}: ${r.status} ${JSON.stringify(r.data)}`)
  return r.data.accessToken
}

async function newTourist(prefix) {
  const email = `${prefix}.${unique()}@turisclick.dev`
  const r = await call('POST', '/api/auth/register', {
    body: { firstName: 'Turista', lastName: 'DePrueba', email, password: 'Password123!' },
  })
  if (r.status !== 200 && r.status !== 201) throw new Error(`registro de turista: ${r.status}`)
  return r.data.accessToken
}

/** Operador con su empresa aprobada, dado de alta por el admin, con la contraseña temporal ya cambiada. */
async function newProvider(adminToken, prefix) {
  const suffix = unique()
  const email = `${prefix}.${suffix}@turisclick.dev`
  const created = await call('POST', '/api/admin/provider-accounts', {
    token: adminToken,
    body: {
      companyName: `Aerolíneas Demo ${suffix}`,
      legalDocument: `NIT-${suffix}`,
      contactEmail: `contacto.${suffix}@turisclick.dev`,
      firstName: 'Operador',
      lastName: 'DePrueba',
      email,
      approve: true,
    },
  })
  if (created.status !== 200 && created.status !== 201) throw new Error(`alta de operador: ${created.status}`)

  const temporary = await login(email, created.data.temporaryPassword)
  const password = 'OperadorTurisClick2026!'
  const changed = await call('POST', '/api/auth/change-password', {
    token: temporary,
    body: { currentPassword: created.data.temporaryPassword, newPassword: password },
  })
  if (changed.status >= 300) throw new Error(`cambio de contraseña: ${changed.status}`)

  return { token: await login(email, password), companyId: created.data.company.id }
}

const flightBody = (origin, destination, number) => ({
  originIata: origin,
  destinationIata: destination,
  carrierName: 'Aerolínea Demo',
  carrierIata: 'ZZ',
  flightNumber: number,
  cabinClass: 'ECONOMY',
  checkedBags: 1,
  carryOnBags: 1,
  cancellationPolicy: [
    { minDaysBefore: 30, refundPercentage: 100 },
    { minDaysBefore: 0, refundPercentage: 0 },
  ],
  fareRules: 'Cambios con cargo. No-show sin reembolso.',
})

const departureBody = (date, hour, seats) => ({
  departureAt: localDateTime(date, hour),
  arrivalAt: localDateTime(date, hour + 1, 10),
  totalSeats: seats,
  adultPrice: 600,
  childPrice: 450,
  infantPrice: 60,
  currency: 'BOB',
})

/** Publica un vuelo con una salida. Devuelve los ids. */
async function publishFlight(provider, origin, destination, number, date, hour, seats) {
  const flight = await call('POST', '/api/companies/me/flights', { token: provider.token, body: flightBody(origin, destination, number) })
  const departure = await call('POST', `/api/companies/me/flights/${flight.data.id}/departures`, {
    token: provider.token,
    body: departureBody(date, hour, seats),
  })
  const published = await call('POST', `/api/companies/me/flights/${flight.data.id}/publish`, { token: provider.token })
  return { flight: flight.data, departure: departure.data, flightStatus: flight.status, departureStatus: departure.status, publishStatus: published.status }
}

const passengers = (flightDate) => {
  const child = new Date(flightDate)
  child.setUTCFullYear(child.getUTCFullYear() - 7)
  const infant = daysFromNow(-60)
  return [
    { type: 'ADULT', givenName: 'Ana', familyName: 'Demostración', bornOn: '1985-03-10' },
    { type: 'ADULT', givenName: 'Luis', familyName: 'Demostración', bornOn: '1987-07-21' },
    { type: 'CHILD', givenName: 'Sofía', familyName: 'Demostración', bornOn: isoDate(child) },
    { type: 'INFANT', givenName: 'Mateo', familyName: 'Demostración', bornOn: isoDate(infant) },
  ]
}

const tickets = (count, seed) => Array.from({ length: count }, (_, i) => `930${seed}${String(i + 1).padStart(6, '0')}`)

async function seatsLeft(departureId, provider, flightId) {
  const list = await call('GET', `/api/companies/me/flights/${flightId}/departures`, { token: provider.token })
  return list.data.find((d) => d.id === departureId)?.availableSeats
}

// ---------------------------------------------------------------- escenarios

async function main() {
  const health = await call('GET', '/api/health')
  if (health.status !== 200) throw new Error(`La API no responde en ${API}`)

  const admin = await login(ADMIN_EMAIL, ADMIN_PASSWORD)
  // Ruta y fecha propias de esta corrida: la base es aislada, pero así una segunda corrida no ve la primera.
  const origin = 'CIJ'
  const destination = 'RIB'
  const date = daysFromNow(40 + Math.floor(Math.random() * 200))
  const returnDate = new Date(date.getTime() + 3 * 86_400_000)

  // ------------------------------------------------------------ 1. autorización
  section('1. El admin autoriza al operador a vender vuelos')
  const provider = await newProvider(admin, 'e2e-operador')

  const notYet = await call('GET', '/api/companies/me/flights', { token: provider.token })
  check('Sin autorización, el catálogo de vuelos del operador responde 403 FLIGHT_SELLER_NOT_AUTHORIZED',
    notYet.status === 403 && notYet.data?.errorCode === 'FLIGHT_SELLER_NOT_AUTHORIZED', `${notYet.status}`)

  const authorize = await call('PUT', `/api/admin/companies/${provider.companyId}/flight-seller`, {
    token: admin,
    body: { authorized: true, registration: 'Registro de agencia E2E-001' },
  })
  check('El admin autoriza la venta de vuelos', authorize.status === 200 && authorize.data.canSellFlights === true)

  const company = await call('GET', '/api/companies/me', { token: provider.token })
  check('La empresa muestra canSellFlights, el registro y la fecha',
    company.data?.canSellFlights === true && company.data?.flightSellerRegistration === 'Registro de agencia E2E-001' && Boolean(company.data?.flightSellerAuthorizedAt))

  // ------------------------------------------------------------ 2. publicación
  section('2. El operador publica vuelos con salidas, asientos y tarifas')
  const outbound = await publishFlight(provider, origin, destination, '701', date, 8, 6)
  check('Vuelo creado (DRAFT) y salida creada', outbound.flightStatus === 201 && outbound.flight.status === 'DRAFT' && outbound.departureStatus === 201)
  check('Publicado', outbound.publishStatus === 200)
  const back = await publishFlight(provider, destination, origin, '702', returnDate, 17, 6)
  check('Vuelo de vuelta publicado', back.publishStatus === 200)
  const mine = await call('GET', '/api/companies/me/flights', { token: provider.token })
  check('El operador ve sus dos vuelos, rotulados demo', mine.data?.length === 2 && mine.data.every((f) => f.demo === true))

  // ------------------------------------------------------------ 3. búsqueda sin sesión
  section('3. El turista busca vuelos sin iniciar sesión')
  const search = await call('POST', '/api/flights/search', {
    body: { originIata: origin, destinationIata: destination, departureDate: isoDate(date), adults: 2, childrenAges: [7], infants: 1 },
  })
  const providerOption = search.data?.options?.find((o) => o.providerFlight?.departureIds?.includes(outbound.departure.id))
  check('La búsqueda anónima responde 200', search.status === 200)
  check('El vuelo de operador aparece reservable', providerOption?.source === 'PROVIDER' && providerOption?.bookable === true)
  check('Rotulado DEMO por su propio flag', providerOption?.providerFlight?.demo === true)
  check('Precio total de la familia: 2×600 + 450 + 60 = 1710 BOB', providerOption?.price?.amount === 1710 && providerOption?.price?.currency === 'BOB')
  check('Las ofertas de aerolínea (simuladas) siguen sin ser reservables',
    search.data?.options?.some((o) => o.source === 'AIRLINE' && !o.bookable) && search.data?.inventoryMode === 'SIMULATED')

  // ------------------------------------------------------------ 4. sólo ida: reservar, pagar, emitir
  section('4. Sólo ida: el turista inicia sesión, reserva, paga; el operador emite')
  const family = passengers(date)
  const noSession = await call('POST', '/api/reservations', {
    body: { flightDepartureIds: [outbound.departure.id], travelers: 3, passengers: family },
  })
  check('Reservar sin sesión es 401', noSession.status === 401)

  const tourist = await newTourist('e2e-turista')
  const reserved = await call('POST', '/api/reservations', {
    token: tourist,
    body: { flightDepartureIds: [outbound.departure.id], travelers: 3, passengers: family },
  })
  const item = reserved.data?.items?.[0]
  check('Reserva creada (PENDING_PAYMENT) con un ítem FLIGHT', reserved.status === 201 && reserved.data.status === 'PENDING_PAYMENT' && item?.productType === 'FLIGHT')
  check('Subtotal 1710 y emisión NOT_REQUIRED antes de pagar', item?.subtotal === 1710 && item?.flight?.issuanceStatus === 'NOT_REQUIRED')
  check('Infante sin asiento: quedan 3 de 6 asientos', (await seatsLeft(outbound.departure.id, provider, outbound.flight.id)) === 3)

  const paid = await call('POST', `/api/reservations/${reserved.data.id}/pay`, { token: tourist, body: { success: true } })
  check('Pago simulado aprobado: reserva CONFIRMED', paid.status === 200 && paid.data.status === 'CONFIRMED' && paid.data.paymentApproved === true)
  check('Emisión PENDING después de pagar (no "emitido")', paid.data?.items?.[0]?.flight?.issuanceStatus === 'PENDING')

  const received = await call('GET', `/api/companies/me/reservations/${item.id}`, { token: provider.token })
  check('El operador ve el ítem con los 4 pasajeros', received.data?.flight?.passengers?.length === 4)

  const issued = await call('POST', `/api/companies/me/reservation-items/${item.id}/issuance`, {
    token: provider.token,
    body: { ticketNumbers: tickets(4, '1'), bookingReference: 'e2e701' },
  })
  check('El operador registra la emisión (un pasaje por pasajero)', issued.status === 200 && issued.data.issuanceStatus === 'ISSUED')

  const seen = await call('GET', `/api/reservations/${reserved.data.id}`, { token: tourist })
  const seenFlight = seen.data?.items?.[0]?.flight
  check('El turista ve ISSUED con 4 pasajes y localizador', seenFlight?.issuanceStatus === 'ISSUED' && seenFlight?.ticketNumbers?.length === 4 && seenFlight?.bookingReference === 'E2E701')
  check('Y el rótulo DEMO', seenFlight?.demo === true)

  // ------------------------------------------------------------ 5. ida y vuelta
  section('5. Ida y vuelta: reservar, pagar, emitir los dos tramos')
  const rtSearch = await call('POST', '/api/flights/search', {
    body: { originIata: origin, destinationIata: destination, departureDate: isoDate(date), returnDate: isoDate(returnDate), adults: 1 },
  })
  const pair = rtSearch.data?.options?.find((o) => o.providerFlight?.operatorCompanyId === provider.companyId)
  check('La búsqueda ofrece el par ida + vuelta del mismo operador',
    pair?.providerFlight?.departureIds?.join() === [outbound.departure.id, back.departure.id].join() && pair?.slices?.length === 2)

  const rtTourist = await newTourist('e2e-turista-rt')
  const rt = await call('POST', '/api/reservations', {
    token: rtTourist,
    body: {
      flightDepartureIds: pair.providerFlight.departureIds,
      travelers: 1,
      passengers: [{ type: 'ADULT', givenName: 'Rosa', familyName: 'Demostración', bornOn: '1979-11-02' }],
    },
  })
  check('Reserva de ida y vuelta con dos ítems', rt.status === 201 && rt.data.items.length === 2)
  const rtPaid = await call('POST', `/api/reservations/${rt.data.id}/pay`, { token: rtTourist, body: { success: true } })
  check('Pagada: los dos tramos quedan con emisión PENDING',
    rtPaid.data?.status === 'CONFIRMED' && rtPaid.data.items.every((i) => i.flight.issuanceStatus === 'PENDING'))
  for (const [index, leg] of rt.data.items.entries()) {
    const r = await call('POST', `/api/companies/me/reservation-items/${leg.id}/issuance`, {
      token: provider.token,
      body: { ticketNumbers: tickets(1, `2${index}`) },
    })
    check(`Emisión registrada para el tramo ${index + 1}`, r.status === 200)
  }
  const rtSeen = await call('GET', `/api/reservations/${rt.data.id}`, { token: rtTourist })
  check('El turista ve los dos tramos ISSUED y DEMO', rtSeen.data?.items?.every((i) => i.flight.issuanceStatus === 'ISSUED' && i.flight.demo === true))

  // ------------------------------------------------------------ 6. emisión fallida
  section('6. Emisión fallida: reembolso total simulado y asientos devueltos')
  const failTourist = await newTourist('e2e-turista-fail')
  const failSeatsBefore = await seatsLeft(back.departure.id, provider, back.flight.id)
  const toFail = await call('POST', '/api/reservations', {
    token: failTourist,
    body: { flightDepartureIds: [back.departure.id], travelers: 1, passengers: [{ type: 'ADULT', givenName: 'Iván', familyName: 'Demostración', bornOn: '1990-01-01' }] },
  })
  await call('POST', `/api/reservations/${toFail.data.id}/pay`, { token: failTourist, body: { success: true } })
  const failure = await call('POST', `/api/companies/me/reservation-items/${toFail.data.items[0].id}/issuance-failure`, {
    token: provider.token,
    body: { reason: 'La aerolínea canceló la ruta.' },
  })
  check('El operador registra que no pudo emitir', failure.status === 200 && failure.data.issuanceStatus === 'FAILED')
  const failed = await call('GET', `/api/reservations/${toFail.data.id}`, { token: failTourist })
  check('Reserva CANCELLED, ítem FAILED', failed.data?.status === 'CANCELLED' && failed.data.items[0].flight.issuanceStatus === 'FAILED')
  check('Reembolso total: refundedAmount = subtotal (600)', failed.data?.items?.[0]?.refundedAmount === 600)
  check('El asiento volvió a la venta', (await seatsLeft(back.departure.id, provider, back.flight.id)) === failSeatsBefore)

  // ------------------------------------------------------------ 7. cancelación
  section('7. Cancelación del turista antes de la emisión: devuelve todo')
  const cancelTourist = await newTourist('e2e-turista-cancel')
  const before = await seatsLeft(back.departure.id, provider, back.flight.id)
  const toCancel = await call('POST', '/api/reservations', {
    token: cancelTourist,
    body: { flightDepartureIds: [back.departure.id], travelers: 1, passengers: [{ type: 'ADULT', givenName: 'Eva', familyName: 'Demostración', bornOn: '1992-02-02' }] },
  })
  await call('POST', `/api/reservations/${toCancel.data.id}/pay`, { token: cancelTourist, body: { success: true } })
  const quote = await call('POST', `/api/reservations/${toCancel.data.id}/cancellation-quote`, { token: cancelTourist })
  check('Presupuesto: 100% por no estar emitido (componente PROVIDER_FLIGHT)',
    quote.data?.lines?.[0]?.component === 'PROVIDER_FLIGHT' && quote.data.lines[0].refundPercentage === 100)
  const cancelled = await call('POST', `/api/reservations/${toCancel.data.id}/cancel`, {
    token: cancelTourist,
    body: { cancellationQuoteId: quote.data.quoteId },
  })
  check('Cancelada con reembolso de 600', cancelled.data?.status === 'CANCELLED' && cancelled.data.items[0].refundedAmount === 600)
  check('Asiento devuelto', (await seatsLeft(back.departure.id, provider, back.flight.id)) === before)

  // ------------------------------------------------------------ 8. carrera por el último asiento
  section('8. Dos turistas compiten por el último asiento')
  const lastSeat = await publishFlight(provider, origin, destination, '799', date, 21, 1)
  const racers = await Promise.all([newTourist('e2e-carrera-a'), newTourist('e2e-carrera-b')])
  const race = await Promise.all(racers.map((token, i) =>
    call('POST', '/api/reservations', {
      token,
      body: { flightDepartureIds: [lastSeat.departure.id], travelers: 1, passengers: [{ type: 'ADULT', givenName: `Corredor${'AB'[i]}`, familyName: 'Demostración', bornOn: '1990-01-01' }] },
    })))
  const statuses = race.map((r) => r.status).sort()
  check('Exactamente uno gana (201) y el otro recibe 409', statuses.join() === '201,409', statuses.join())
  check('Asientos libres: 0', (await seatsLeft(lastSeat.departure.id, provider, lastSeat.flight.id)) === 0)

  // ------------------------------------------------------------ 9. paquete con vuelo (regresión)
  section('9. Regresión: paquete con vuelo (proveedor aéreo simulado)')
  const cities = (await call('GET', '/api/destinations?type=CITY')).data
  const city = cities.find((c) => c.name === 'Uyuni') ?? cities[0]
  const pkg = await call('POST', '/api/packages', {
    token: provider.token,
    body: {
      title: `Salar E2E ${unique()}`,
      description: 'Cuatro días por el salar, con guía local, traslados y alojamiento incluidos.',
      destinationId: city.id,
      categoryIds: [],
      durationDays: 4,
      price: 480,
      currency: 'USD',
      items: [{ dayNumber: 1, sortOrder: 1, kind: 'DESCRIPTIVE', title: 'Llegada' }],
      images: [],
    },
  })
  const availability = await call('POST', `/api/packages/${pkg.data.id}/availability`, {
    token: provider.token,
    body: { departureDate: isoDate(daysFromNow(45)), totalSlots: 5 },
  })
  await call('POST', `/api/packages/${pkg.data.id}/publish`, { token: provider.token })
  const rule = await call('PUT', `/api/packages/${pkg.data.id}/flight-rule`, {
    token: provider.token,
    body: { destinationIata: 'UYU', allowedOriginIatas: ['VVI'], cabinClass: 'ECONOMY', outboundOffsetDays: 0, inboundOffsetDays: 0, roundTrip: true },
  })
  check('Paquete con regla de vuelo publicado', pkg.status === 201 && availability.status === 201 && rule.status === 200,
    `${pkg.status}/${availability.status}/${rule.status}`)

  const pkgTourist = await newTourist('e2e-paquete')
  const pkgQuote = await call('POST', `/api/packages/${pkg.data.id}/flight-quotes`, {
    token: pkgTourist,
    body: { originIata: 'VVI', packageAvailabilityId: availability.data.id, travelers: 1 },
  })
  check('Cotización de vuelo del paquete (simulada)', pkgQuote.status === 200 && pkgQuote.data.options.length > 0 && pkgQuote.data.testMode === true)
  const pkgReservation = await call('POST', '/api/reservations', {
    token: pkgTourist,
    body: { packageAvailabilityId: availability.data.id, travelers: 1, flightQuoteId: pkgQuote.data.options[0].quoteId },
  })
  const pkgPaid = await call('POST', `/api/reservations/${pkgReservation.data.id}/pay`, {
    token: pkgTourist,
    body: {
      success: true,
      acceptPriceChanges: false,
      travelers: [{ givenName: 'Ana', familyName: 'Quiroga', bornOn: '1990-05-14', gender: 'f', title: 'ms', email: 'ana.quiroga@example.test', phoneNumber: '+59170000000' }],
    },
  })
  check('Reserva de paquete con vuelo pagada: CONFIRMED', pkgPaid.status === 200 && pkgPaid.data.status === 'CONFIRMED', `${pkgPaid.status}`)
  check('Vuelo del paquete CONFIRMED con inventario SIMULATED (nunca "pasaje emitido")',
    pkgPaid.data?.flight?.status === 'CONFIRMED' && pkgPaid.data.flight.inventoryMode === 'SIMULATED' && !/pasaje emitido/i.test(pkgPaid.data.flight.statusMessage))
  check('El ítem del paquete no es un vuelo de operador', pkgPaid.data?.items?.[0]?.productType === 'PACKAGE' && pkgPaid.data.items[0].flight == null)

  // ------------------------------------------------------------ 10. admin
  section('10. Admin: reservas de vuelos y moderación')
  const flightsOnly = await call('GET', '/api/admin/reservations?productType=FLIGHT&pageSize=100', { token: admin })
  check('Filtro productType=FLIGHT', flightsOnly.status === 200 && flightsOnly.data.items.some((r) => r.id === reserved.data.id))
  const suspended = await call('POST', `/api/admin/flights/${outbound.flight.id}/suspend`, { token: admin })
  const afterSuspend = await call('POST', '/api/flights/search', { body: { originIata: origin, destinationIata: destination, departureDate: isoDate(date), adults: 1 } })
  check('Un vuelo suspendido sale de la búsqueda',
    suspended.status === 200 && !afterSuspend.data.options.some((o) => o.providerFlight?.departureIds?.includes(outbound.departure.id)))
  await call('POST', `/api/admin/flights/${outbound.flight.id}/unsuspend`, { token: admin })

  // ------------------------------------------------------------ resumen
  const failedChecks = results.filter((r) => !r.ok)
  console.log(`\n${results.length - failedChecks.length}/${results.length} verificaciones OK`)
  if (failedChecks.length) {
    for (const f of failedChecks) console.log(`  FAIL [${f.section}] ${f.name}${f.detail ? ` — ${f.detail}` : ''}`)
    process.exit(1)
  }
}

main().catch((error) => {
  console.error(`\nEl E2E se interrumpió: ${error.message}`)
  process.exit(1)
})
