// Validación REAL contra Azure V2 de la oleada calendario + imágenes de destinos + onboarding + asistente IA.
// Usa cuentas demo/QA propias (nunca imprime contraseñas ni tokens). Lo que escribe es acotado y reversible:
// - agrega salidas de abril 2027 (fines de semana) a UNA experiencia del proveedor demo (re-ejecutar no duplica);
// - una cuenta QA de turista (qa.asistente@turisclick.dev) guarda preferencias, conversa con el asistente,
//   reserva y CANCELA (las reservas quedan CANCELLED y el cupo vuelve).
//
// Uso: .\run-catalog.ps1 validate-product-wave.mjs
const API = process.env.TURISCLICK_API ?? 'https://app-turisclick-v2-api.azurewebsites.net'
const env = (name) => {
  if (!process.env[name]) throw new Error(`falta ${name}`)
  return process.env[name]
}

let failures = 0
let checks = 0
const ok = (cond, label, extra = '') => {
  checks++
  if (!cond) failures++
  console.log(`${cond ? 'OK  ' : 'FAIL'} ${label}${extra ? ' — ' + extra : ''}`)
  return cond
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
async function call(method, path, { body, token } = {}) {
  for (let attempt = 1; ; attempt++) {
    try {
      const started = Date.now()
      const res = await fetch(API + path, {
        method,
        headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
        body: body ? JSON.stringify(body) : undefined,
        signal: AbortSignal.timeout(120_000),
      })
      const text = await res.text()
      let data = null
      try { data = text ? JSON.parse(text) : null } catch { data = text }
      if (res.status >= 502 && attempt < 4) { await sleep(5000 * attempt); continue }
      return { status: res.status, data, ms: Date.now() - started }
    } catch (err) {
      if (attempt >= 4) throw err
      await sleep(5000 * attempt)
    }
  }
}
const items = (d) => (Array.isArray(d) ? d : d?.items ?? [])
const login = async (email, password) => {
  const r = await call('POST', '/api/auth/login', { body: { email, password } })
  if (r.status !== 200) throw new Error(`login ${email} -> ${r.status}`)
  return r.data
}

// ───────────────────────── 1. Catálogo e imágenes de destinos ─────────────────────────
console.log('===== CATÁLOGO E IMÁGENES DE DESTINOS =====')
const exps = await call('GET', '/api/experiences?page=1&pageSize=100')
const pkgs = await call('GET', '/api/packages?page=1&pageSize=100')
ok(exps.data.totalCount === 90 && pkgs.data.totalCount === 13, 'catálogo demo intacto (90 experiencias, 13 paquetes)', `${exps.data.totalCount}/${pkgs.data.totalCount}`)
const destinations = items((await call('GET', '/api/destinations?type=CITY')).data)
const withImage = destinations.filter((d) => d.imageUrl)
ok(withImage.length >= 41, 'ciudades con imagen representativa', `${withImage.length}/${destinations.length}`)
for (const name of ['Santa Cruz de la Sierra', 'La Paz', 'Cochabamba', 'Sucre', 'Tarija', 'Uyuni', 'Potosí', 'Oruro', 'Copacabana', 'Coroico', 'Rurrenabaque', 'Samaipata', 'Torotoro', 'Villa Tunari', 'Tupiza']) {
  ok(Boolean(destinations.find((d) => d.name === name)?.imageUrl), `· imagen de ${name}`)
}
const sample = withImage.find((d) => d.name === 'Uyuni')
const head = await fetch(sample.imageUrl, { method: 'HEAD', headers: { 'User-Agent': 'TurisClickDemoCatalog/1.0' } })
ok(head.status === 200 && (head.headers.get('content-type') ?? '').startsWith('image/'), 'la imagen de destino carga', `${head.status}`)

// ───────────────────────── 2. Calendario de disponibilidad ─────────────────────────
console.log('\n===== CALENDARIO DE DISPONIBILIDAD =====')
const provider = await login('proveedor.demo@turisclick.dev', env('PROVIDER_PASSWORD'))
const mine = items((await call('GET', '/api/experiences/mine?page=1&pageSize=100', { token: provider.accessToken })).data)
const teleferico = mine.find((e) => e.title === 'La Paz desde el Teleférico')
const bulkBody = { startDate: '2027-04-01', endDate: '2027-04-30', preset: 'WEEKENDS', startTimes: ['09:30:00'], totalSlots: 15 }
const preview = await call('POST', `/api/experiences/${teleferico.id}/availability/bulk`, { token: provider.accessToken, body: { ...bulkBody, dryRun: true } })
ok(preview.status === 200 && preview.data.requestedCount === 8, 'vista previa (dryRun): fines de semana de abril 2027 = 8 fechas', `${preview.status} req=${preview.data?.requestedCount}`)
const created = await call('POST', `/api/experiences/${teleferico.id}/availability/bulk`, { token: provider.accessToken, body: bulkBody })
ok(created.status === 201 && created.data.createdCount + created.data.skippedCount === 8, 'generación masiva real', `creadas=${created.data?.createdCount} omitidas=${created.data?.skippedCount}`)
ok((created.data.created ?? []).every((s) => [0, 6].includes(new Date(`${s.date}T00:00:00Z`).getUTCDay())), 'solo sábados y domingos')
const again = await call('POST', `/api/experiences/${teleferico.id}/availability/bulk`, { token: provider.accessToken, body: bulkBody })
ok(again.status === 201 && again.data.createdCount === 0 && again.data.skippedCount === 8, 'repetir no duplica (todas omitidas)')
const past = await call('POST', `/api/experiences/${teleferico.id}/availability/bulk`, { token: provider.accessToken, body: { ...bulkBody, startDate: '2025-01-01', endDate: '2025-01-31' } })
ok(past.status === 400, 'fechas pasadas → 400', `${past.status}`)
const owned = items((await call('GET', `/api/experiences/mine/${teleferico.id}/availability`, { token: provider.accessToken })).data)
const aprilSlot = owned.find((s) => s.date === '2027-04-03')
const closed = await call('PATCH', `/api/experiences/${teleferico.id}/availability/${aprilSlot.id}`, { token: provider.accessToken, body: { status: 'CLOSED' } })
const publicSlots = items((await call('GET', `/api/experiences/${teleferico.id}/availability`)).data)
ok(closed.data?.status === 'CLOSED' && !publicSlots.some((s) => s.id === aprilSlot.id), 'cerrar una fecha la quita del calendario público')
const reopened = await call('PATCH', `/api/experiences/${teleferico.id}/availability/${aprilSlot.id}`, { token: provider.accessToken, body: { status: 'OPEN' } })
ok(reopened.data?.status === 'OPEN', 'reabrir la fecha')
const otherProvider = await login('proveedor.charcas@turisclick.dev', env('CATALOG_PROVIDER_PASSWORD'))
const foreign = await call('POST', `/api/experiences/${teleferico.id}/availability/bulk`, { token: otherProvider.accessToken, body: { ...bulkBody, dryRun: true } })
ok(foreign.status === 403, 'otro proveedor no puede programar fechas ajenas', `${foreign.status}`)

// ───────────────────────── 3. Auth + onboarding ─────────────────────────
console.log('\n===== AUTH Y ONBOARDING =====')
const qaEmail = 'qa.asistente@turisclick.dev'
const qaPassword = env('QA_AI_TOURIST_PASSWORD')
const reg = await call('POST', '/api/auth/register', { body: { firstName: 'Quimey', lastName: 'Asistente', email: qaEmail, password: qaPassword } })
ok([200, 201, 409].includes(reg.status), 'registro de turista QA (o ya existente)', `${reg.status}`)
const tourist = await login(qaEmail, qaPassword)
const token = tourist.accessToken
ok(tourist.user.role === 'TOURIST', 'login turista')
const categories = items((await call('GET', '/api/categories')).data)
const catId = (name) => categories.find((c) => c.name === name).id
const saved = await call('PUT', '/api/tourists/me/preferences', {
  token,
  body: { categoryIds: [catId('Naturaleza'), catId('Gastronomía')], travelPace: 'RELAXED', travelParty: 'SOLO', budgetLevel: 'MODERATE', completeOnboarding: true },
})
ok(saved.status === 200 && saved.data.onboardingCompleted, 'onboarding guardado')
const reloaded = await call('GET', '/api/tourists/me/preferences', { token })
ok(reloaded.data.categories.length === 2 && reloaded.data.travelParty === 'SOLO' && reloaded.data.travelPace === 'RELAXED', 'preferencias persistidas en PostgreSQL')
const updated = await call('PUT', '/api/tourists/me/preferences', {
  token,
  body: { categoryIds: [catId('Naturaleza'), catId('Gastronomía')], travelPace: 'RELAXED', travelParty: 'SOLO', budgetLevel: 'MODERATE', completeOnboarding: false },
})
ok(updated.data.onboardingCompleted === true, 'editar desde Perfil no vuelve a dejar pendiente el onboarding')
ok((await call('GET', '/api/tourists/me/preferences', { token: provider.accessToken })).status === 403, 'un proveedor no accede a preferencias de turista')

// ───────────────────────── 4. Reserva sobre el calendario ─────────────────────────
console.log('\n===== RESERVA =====')
const bookable = items((await call('GET', `/api/experiences/${teleferico.id}/availability`)).data)
const slot = bookable.at(-1)
const reservation = await call('POST', '/api/reservations', { token, body: { experienceAvailabilityId: slot.id, travelers: 1 } })
ok(reservation.status === 201 && reservation.data.status === 'PENDING_PAYMENT', 'reservar una fecha del calendario', `${reservation.status}`)
const cancelled = await call('POST', `/api/reservations/${reservation.data.id}/cancel`, { token })
ok(cancelled.data?.status === 'CANCELLED', 'cancelar libera el cupo', `${cancelled.status}`)

// ───────────────────────── 5. Asistente IA con preferencias ─────────────────────────
console.log('\n===== ASISTENTE IA =====')
const conversation = (await call('POST', '/api/ai/conversations', { token })).data
const first = await call('POST', `/api/ai/conversations/${conversation.id}/messages`, { token, body: { content: 'Voy 3 días a La Paz.' } })
const itinerary = first.data?.itinerary
ok(first.status === 200 && !first.data.clarificationNeeded, 'no repregunta intereses ni viajeros (vienen del perfil)', `${first.status} ${first.ms} ms`)
ok((first.data?.profileHints ?? []).some((h) => h.includes('Naturaleza')), 'informa qué tomó del perfil', (first.data?.profileHints ?? []).join(' | '))
ok(Boolean(itinerary?.items?.length), 'genera un itinerario', `${itinerary?.items?.length ?? 0} ítems`)
let realProducts = true
for (const item of itinerary?.items ?? []) {
  const path = item.productType === 'PACKAGE' ? `/api/packages/${item.packageId}` : `/api/experiences/${item.experienceId}`
  const product = await call('GET', path)
  if (product.status !== 200 || product.data.price !== item.estimatedUnitPrice || product.data.currency !== item.currency) realProducts = false
}
ok(realProducts, 'cada ítem es un producto real publicado, con su precio real')

const explanation = await call('GET', `/api/ai/itineraries/${itinerary.id}/items/${itinerary.items[0].id}/explanation`, { token })
ok(explanation.status === 200 && explanation.data.explanation && explanation.data.facts.length > 0, 'explicación basada en hechos del backend')

const cheaper = await call('POST', `/api/ai/conversations/${conversation.id}/messages`, { token, body: { content: 'Quiero algo más barato' } })
ok(cheaper.status === 200 && (cheaper.data.itinerary?.version ?? 0) > itinerary.version, 'iterar: "más barato" genera una nueva versión', `v${cheaper.data?.itinerary?.version}`)

const cultural = await call('POST', `/api/ai/conversations/${conversation.id}/messages`, { token, body: { content: 'Esta vez quiero algo tranquilo y cultural en Sucre, 2 días.' } })
const culturalItems = cultural.data?.itinerary?.items ?? []
ok(cultural.data?.parsedPreferences?.preferredDestinationName === 'Sucre', 'la consulta actual cambia el destino (Sucre)')
ok(cultural.data?.parsedPreferences?.categories?.some((c) => c.name === 'Cultura'), 'la consulta actual manda: interés Cultura')
ok(!(cultural.data?.profileHints ?? []).some((h) => h.startsWith('Tus intereses')), 'con intereses en la consulta, no usa los del perfil')
let inSucre = culturalItems.length > 0
for (const item of culturalItems) {
  const path = item.productType === 'PACKAGE' ? `/api/packages/${item.packageId}` : `/api/experiences/${item.experienceId}`
  if ((await call('GET', path)).data?.destinationName !== 'Sucre') inSucre = false
}
ok(inSucre, 'el nuevo itinerario es de Sucre', `${culturalItems.length} ítems`)

const current = cultural.data.itinerary
const savedItinerary = await call('POST', `/api/ai/itineraries/${current.id}/save`, { token })
ok(savedItinerary.data?.status === 'SAVED', 'guardar itinerario')
const savedList = items((await call('GET', '/api/ai/itineraries/me', { token })).data)
ok(savedList.some((i) => i.id === current.id), 'retomar: aparece en guardados')
const resumed = await call('GET', `/api/ai/conversations/${conversation.id}`, { token })
ok(resumed.data?.messages?.length >= 6, 'retomar: la conversación conserva el historial', `${resumed.data?.messages?.length} mensajes`)

const booking = await call('POST', `/api/ai/itineraries/${current.id}/book`, { token, body: { acceptPriceChanges: false } })
ok(booking.status === 200 && booking.data.reservation?.status === 'PENDING_PAYMENT', 'reserva atómica del itinerario', `${booking.status} ${booking.data?.reservation?.status ?? booking.data?.detail}`)
if (booking.data?.reservation?.id) {
  const undo = await call('POST', `/api/reservations/${booking.data.reservation.id}/cancel`, { token })
  ok(undo.data?.status === 'CANCELLED', 'se cancela la reserva de prueba y vuelve el cupo')
}
const foreignConversation = await call('GET', `/api/ai/conversations/${conversation.id}`, {
  token: (await login('turista.demo@turisclick.dev', env('DEMO_TOURIST_PASSWORD'))).accessToken,
})
ok(foreignConversation.status === 403, 'otro turista no puede ver la conversación', `${foreignConversation.status}`)

console.log(`\nRESULTADO: ${failures === 0 ? 'TODO OK' : failures + ' fallas'} (${checks} verificaciones)`)
process.exit(failures === 0 ? 0 : 1)
