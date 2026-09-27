// E2E de producto contra Azure V2: los mismos endpoints, en el mismo orden, que recorren el Backoffice
// (ADMIN y PROVIDER) y Tourist Mobile (turista y asistente IA). Sólo cuentas demo/QA; nunca imprime
// contraseñas ni tokens.
//
// Lo que escribe es acotado y se limpia o se deja aislado del catálogo público:
// - ADMIN: crea y borra un país/región/ciudad y una categoría de prueba (no queda nada en el catálogo).
// - PROVIDER QA (qa.proveedor.e2e@turisclick.dev): sus productos cuelgan de una ciudad real sin catálogo y
//   quedan en DRAFT, así que el catálogo público sigue en 90/13; al final su empresa queda suspendida.
// - TURISTA QA (qa.turista.e2e@turisclick.dev): reserva y paga sobre una fecha de abril 2027 creada por QA,
//   y las reservas del asistente se cancelan al terminar.
//
// Uso: .\run-catalog.ps1 validate-e2e.mjs
const API = process.env.TURISCLICK_API ?? 'https://app-turisclick-v2-api.azurewebsites.net'
const env = (name) => {
  if (!process.env[name]) throw new Error(`falta ${name}`)
  return process.env[name]
}
const suffix = Date.now().toString(36).slice(-6)

let failures = 0
let checks = 0
const ok = (cond, label, extra = '') => {
  checks++
  if (!cond) failures++
  console.log(`${cond ? 'OK  ' : 'FAIL'} ${label}${extra ? ' — ' + extra : ''}`)
  return cond
}
const section = (title) => console.log(`\n===== ${title} =====`)
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function call(method, path, { body, token } = {}) {
  for (let attempt = 1; ; attempt++) {
    try {
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
      return { status: res.status, data }
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

// ───────────────────────── ADMIN (Backoffice) ─────────────────────────
section('ADMIN')
const admin = await login('admin@turisclick.dev', env('ADMIN_PASSWORD'))
const adminToken = admin.accessToken
ok(admin.user.role === 'ADMIN', 'login ADMIN')

const users = await call('GET', '/api/admin/users?page=1&pageSize=5&role=TOURIST', { token: adminToken })
ok(users.status === 200 && items(users.data).every((u) => u.role === 'TOURIST'), 'usuarios: listado con filtro por rol', `${users.data?.totalCount} turistas`)
const pending = await call('GET', '/api/admin/companies?status=APPROVED&page=1&pageSize=50', { token: adminToken })
ok(pending.status === 200 && items(pending.data).length >= 8, 'empresas: listado por estado', `${items(pending.data).length} aprobadas`)

const country = await call('POST', '/api/admin/destinations', { token: adminToken, body: { name: `QA-País-${suffix}`, type: 'COUNTRY' } })
const region = await call('POST', '/api/admin/destinations', { token: adminToken, body: { name: `QA-Región-${suffix}`, type: 'REGION', parentId: country.data.id } })
const city = await call('POST', '/api/admin/destinations', { token: adminToken, body: { name: `QA-Ciudad-${suffix}`, type: 'CITY', parentId: region.data.id } })
ok([country, region, city].every((r) => r.status === 201), 'destinos: alta de jerarquía país → región → ciudad')
const imageUrl = 'https://upload.wikimedia.org/wikipedia/commons/thumb/6/61/Watching_Sunset_Salar_de_Uyuni_Bolivia_Luca_Galuzzi_2006.jpg/1280px-Watching_Sunset_Salar_de_Uyuni_Bolivia_Luca_Galuzzi_2006.jpg'
const withImage = await call('PUT', `/api/admin/destinations/${city.data.id}`, { token: adminToken, body: { name: city.data.name, imageUrl } })
ok(withImage.data?.imageUrl === imageUrl, 'destinos: imagen representativa guardada')
const badImage = await call('PUT', `/api/admin/destinations/${city.data.id}`, { token: adminToken, body: { name: city.data.name, imageUrl: 'data:image/png;base64,AAA' } })
ok(badImage.status === 400, 'destinos: rechaza una imagen que no es URL http(s)', `${badImage.status}`)

// Los productos QA cuelgan de una ciudad real sin experiencias publicadas: así ningún destino de prueba
// aparece en la app, y la jerarquía QA de arriba se puede borrar entera al final.
const cities = items((await call('GET', '/api/destinations?type=CITY')).data)
const qaCity = cities.find((c) => c.name === 'Cobija') ?? cities.find((c) => c.publishedExperienceCount === 0)
ok(Boolean(qaCity), 'ciudad real sin catálogo para los productos QA', qaCity?.name)

const category = await call('POST', '/api/admin/categories', { token: adminToken, body: { name: `QA-Cat-${suffix}`, description: 'Categoría de prueba E2E' } })
const renamed = await call('PUT', `/api/admin/categories/${category.data.id}`, { token: adminToken, body: { name: `QA-Cat-${suffix}-v2` } })
ok(category.status === 201 && renamed.status === 200, 'categorías: alta y edición')

// ───────────────────────── PROVIDER (Backoffice) ─────────────────────────
section('PROVIDER')
const providerEmail = 'qa.proveedor.e2e@turisclick.dev'
const providerPassword = env('QA_E2E_PROVIDER_PASSWORD')
const registration = await call('POST', '/api/providers/register', {
  body: {
    firstName: 'Quimey', lastName: 'Proveedor', email: providerEmail, password: providerPassword,
    companyName: 'QA E2E Operadora', companyDescription: 'Empresa de prueba automatizada (no aparece en el catálogo).',
    legalDocument: 'NIT-QA-E2E', contactEmail: 'qa.e2e@turisclick.dev',
  },
})
ok([201, 409].includes(registration.status), 'registro de proveedor QA (o ya existente)', `${registration.status}`)

let provider = await login(providerEmail, providerPassword)
const company = await call('GET', `/api/admin/companies/${provider.user.companyId}`, { token: adminToken })
if (company.data.status === 'SUSPENDED') {
  ok((await call('POST', `/api/admin/companies/${company.data.id}/reactivate`, { token: adminToken })).status === 200, 'moderación: reactivar empresa suspendida')
} else if (company.data.status !== 'APPROVED') {
  ok((await call('POST', `/api/admin/companies/${company.data.id}/approve`, { token: adminToken })).status === 200, 'moderación: aprobar empresa')
} else {
  ok(true, 'empresa QA ya estaba aprobada')
}
provider = await login(providerEmail, providerPassword)
const providerToken = provider.accessToken

const mine = await call('GET', '/api/companies/me', { token: providerToken })
const updatedCompany = await call('PUT', '/api/companies/me', {
  token: providerToken,
  body: { name: mine.data.name, description: `Actualizada por el E2E ${suffix}`, contactEmail: mine.data.contactEmail, contactPhone: null },
})
ok(updatedCompany.status === 200 && updatedCompany.data.description.includes(suffix), 'mi empresa: lectura y edición')

const categories = items((await call('GET', '/api/categories')).data)
const experience = await call('POST', '/api/experiences', {
  token: providerToken,
  body: {
    title: `QA E2E Tour ${suffix}`, description: 'Experiencia de prueba creada por el E2E automatizado.',
    destinationId: qaCity.id, categoryIds: [categories[0].id], durationMinutes: 180, durationLabel: '3 horas',
    price: 150, currency: 'BOB', images: [{ url: imageUrl, isCover: true }],
  },
})
ok(experience.status === 201 && experience.data.status === 'DRAFT', 'experiencia: alta en borrador', `${experience.status}`)
const edited = await call('PUT', `/api/experiences/${experience.data.id}`, {
  token: providerToken,
  body: { ...experience.data, title: `QA E2E Tour ${suffix} (editado)`, categoryIds: [categories[0].id], images: [{ url: imageUrl, isCover: true }] },
})
ok(edited.data?.title.endsWith('(editado)'), 'experiencia: edición')

const presets = [
  { preset: 'EVERY_DAY', expected: 7 },
  { preset: 'WEEKDAYS', expected: 5 },
  { preset: 'WEEKENDS', expected: 2 },
  { preset: 'CUSTOM', weekdays: [2, 4], expected: 2 },
]
for (const { preset, weekdays, expected } of presets) {
  const preview = await call('POST', `/api/experiences/${experience.data.id}/availability/bulk`, {
    token: providerToken,
    body: { startDate: '2027-06-07', endDate: '2027-06-13', preset, weekdays: weekdays ?? [], startTimes: ['09:00:00'], totalSlots: 10, dryRun: true },
  })
  ok(preview.status === 200 && preview.data.requestedCount === expected, `calendario: preset ${preset} → ${expected} fechas`, `${preview.data?.requestedCount}`)
}
const generated = await call('POST', `/api/experiences/${experience.data.id}/availability/bulk`, {
  token: providerToken,
  body: { startDate: '2027-06-07', endDate: '2027-06-20', preset: 'WEEKDAYS', startTimes: ['09:00:00', '15:00:00'], totalSlots: 10 },
})
ok(generated.status === 201 && generated.data.createdCount === 20, 'calendario: 10 días hábiles × 2 horarios', `${generated.data?.createdCount}`)
const slot = generated.data.created[0]
const resized = await call('PATCH', `/api/experiences/${experience.data.id}/availability/${slot.id}`, { token: providerToken, body: { totalSlots: 25, status: 'CLOSED' } })
ok(resized.data?.totalSlots === 25 && resized.data?.status === 'CLOSED', 'calendario: cambiar cupo y cerrar una fecha')

const pkg = await call('POST', '/api/packages', {
  token: providerToken,
  body: {
    title: `QA E2E Paquete ${suffix}`, description: 'Paquete de prueba creado por el E2E automatizado.',
    destinationId: qaCity.id, categoryIds: [categories[0].id], conditionsText: 'Prueba', durationDays: 2, price: 600, currency: 'BOB',
    items: [{ dayNumber: 1, sortOrder: 1, kind: 'EXPERIENCE_REFERENCE', experienceId: experience.data.id }],
    images: [{ url: imageUrl, isCover: true }],
  },
})
ok(pkg.status === 201, 'paquete: alta con referencia a una experiencia propia', `${pkg.status}`)
const pkgDates = await call('POST', `/api/packages/${pkg.data.id}/availability/bulk`, {
  token: providerToken,
  body: { startDate: '2027-06-07', endDate: '2027-06-27', preset: 'WEEKENDS', totalSlots: 8 },
})
ok(pkgDates.status === 201 && pkgDates.data.createdCount === 6, 'paquete: salidas de fin de semana', `${pkgDates.data?.createdCount}`)

// Ownership: el proveedor demo no puede tocar nada de la empresa QA, ni al revés.
const demoProvider = await login('proveedor.demo@turisclick.dev', env('PROVIDER_PASSWORD'))
const foreignEdit = await call('PUT', `/api/experiences/${experience.data.id}`, { token: demoProvider.accessToken, body: { ...experience.data, categoryIds: [], images: [] } })
const foreignBulk = await call('POST', `/api/experiences/${experience.data.id}/availability/bulk`, { token: demoProvider.accessToken, body: { startDate: '2027-07-01', endDate: '2027-07-02', preset: 'EVERY_DAY', totalSlots: 5, dryRun: true } })
const foreignRead = await call('GET', `/api/experiences/mine/${experience.data.id}`, { token: demoProvider.accessToken })
ok([foreignEdit.status, foreignBulk.status, foreignRead.status].every((s) => s === 403), 'ownership: otro proveedor no edita, programa ni lee lo ajeno', `${foreignEdit.status}/${foreignBulk.status}/${foreignRead.status}`)
const reservations = await call('GET', '/api/companies/me/reservations?page=1&pageSize=5', { token: providerToken })
ok(reservations.status === 200, 'reservas recibidas por el proveedor', `${reservations.data?.totalCount ?? 0}`)

// ───────────────────────── TURISTA (Tourist Mobile) ─────────────────────────
section('TURISTA')
const touristEmail = 'qa.turista.e2e@turisclick.dev'
const touristPassword = env('QA_E2E_TOURIST_PASSWORD')
const signup = await call('POST', '/api/auth/register', { body: { firstName: 'Quena', lastName: 'Turista', email: touristEmail, password: touristPassword } })
ok([200, 201, 409].includes(signup.status), 'crear cuenta de turista (o ya existente)', `${signup.status}`)
const tourist = await login(touristEmail, touristPassword)
const touristToken = tourist.accessToken

const prefs = await call('PUT', '/api/tourists/me/preferences', {
  token: touristToken,
  body: {
    categoryIds: [categories.find((c) => c.name === 'Naturaleza').id, categories.find((c) => c.name === 'Gastronomía').id],
    travelPace: 'BALANCED', travelParty: 'COUPLE', budgetLevel: 'MODERATE', completeOnboarding: true,
  },
})
ok(prefs.status === 200 && prefs.data.onboardingCompleted, 'onboarding completado')

const destinations = items((await call('GET', '/api/destinations?type=CITY')).data)
const laPaz = destinations.find((d) => d.name === 'La Paz')
ok(Boolean(laPaz?.imageUrl) && laPaz.publishedExperienceCount > 0, 'home: destinos con imagen y conteo real', `${destinations.filter((d) => d.imageUrl).length} con imagen`)
const byCity = await call('GET', `/api/experiences?destinationId=${laPaz.id}&page=1&pageSize=50`)
ok(byCity.data?.totalCount === laPaz.publishedExperienceCount, 'explorar: filtro por destino coincide con el conteo del destino')

const teleferico = items(byCity.data).find((e) => e.title === 'La Paz desde el Teleférico')
const detail = await call('GET', `/api/experiences/${teleferico.id}`)
ok(detail.status === 200 && detail.data.images.length > 0, 'detalle de experiencia con imágenes')
const availability = items((await call('GET', `/api/experiences/${teleferico.id}/availability`)).data)
const april2027 = availability.filter((s) => s.date.startsWith('2027-04') && s.availableSlots > 0)
ok(april2027.length > 0, 'calendario público con fechas futuras', `${availability.length} fechas, ${april2027.length} en abril 2027`)

const target = april2027.at(-1)
const reservation = await call('POST', '/api/reservations', { token: touristToken, body: { experienceAvailabilityId: target.id, travelers: 2 } })
ok(reservation.status === 201 && reservation.data.status === 'PENDING_PAYMENT', 'reserva creada (2 viajeros)', `${reservation.status}`)
const paid = await call('POST', `/api/reservations/${reservation.data.id}/pay`, { token: touristToken, body: { success: true } })
ok(paid.status === 200 && paid.data.status === 'CONFIRMED', 'checkout: pago simulado → CONFIRMED')
const trips = await call('GET', '/api/reservations/me?page=1&pageSize=10', { token: touristToken })
ok(items(trips.data).some((r) => r.id === reservation.data.id), 'mis viajes incluye la reserva confirmada')
const afterPay = items((await call('GET', `/api/experiences/${teleferico.id}/availability`)).data).find((s) => s.id === target.id)
ok((afterPay?.availableSlots ?? 0) === target.availableSlots - 2, 'el cupo quedó descontado', `${target.availableSlots} → ${afterPay?.availableSlots}`)

// ───────────────────────── ASISTENTE IA ─────────────────────────
section('ASISTENTE IA')
const conversation = (await call('POST', '/api/ai/conversations', { token: touristToken })).data
const first = await call('POST', `/api/ai/conversations/${conversation.id}/messages`, { token: touristToken, body: { content: 'Voy 3 días a La Paz.' } })
ok(!first.data.clarificationNeeded && first.data.itinerary, 'recomendación sin repreguntar (usa el perfil)', (first.data.profileHints ?? []).join(' | '))
const itinerary = first.data.itinerary
ok(itinerary.items.every((i) => i.experienceId || i.packageId), 'el itinerario sólo tiene productos reales')
const why = await call('GET', `/api/ai/itineraries/${itinerary.id}/items/${itinerary.items[0].id}/explanation`, { token: touristToken })
ok(why.status === 200 && why.data.facts.length > 0, '"¿Por qué?" con hechos verificados', `${why.data?.facts?.length} hechos`)
const cheaper = await call('POST', `/api/ai/conversations/${conversation.id}/messages`, { token: touristToken, body: { content: 'Quiero algo más barato' } })
ok((cheaper.data?.itinerary?.version ?? 0) > itinerary.version, 'modificar el itinerario sube la versión', `v${cheaper.data?.itinerary?.version}`)
const current = cheaper.data.itinerary
ok((await call('POST', `/api/ai/itineraries/${current.id}/save`, { token: touristToken })).data?.status === 'SAVED', 'guardar itinerario')
const resumed = await call('GET', `/api/ai/itineraries/${current.id}`, { token: touristToken })
ok(resumed.status === 200 && resumed.data.items.length === current.items.length, 'retomar el itinerario guardado (revalidado)')
const booked = await call('POST', `/api/ai/itineraries/${current.id}/book`, { token: touristToken, body: { acceptPriceChanges: true } })
ok(booked.status === 200 && booked.data.reservation?.status === 'PENDING_PAYMENT', 'reservar el itinerario completo', `${booked.data?.reservation?.items?.length ?? 0} componentes`)
if (booked.data?.reservation?.id) {
  ok((await call('POST', `/api/reservations/${booked.data.reservation.id}/cancel`, { token: touristToken })).data?.status === 'CANCELLED', 'se cancela la reserva del itinerario (QA) y vuelve el cupo')
}

// ───────────────────────── Limpieza ─────────────────────────
section('LIMPIEZA')
ok((await call('POST', `/api/admin/companies/${provider.user.companyId}/suspend`, { token: adminToken })).status === 200, 'empresa QA suspendida (fuera del catálogo)')
ok((await call('DELETE', `/api/admin/categories/${category.data.id}`, { token: adminToken })).status === 204, 'categoría de prueba borrada')
// De hoja a raíz: la jerarquía de prueba no tiene productos colgando, así que se borra entera.
const removed = []
for (const id of [city.data.id, region.data.id, country.data.id]) {
  removed.push((await call('DELETE', `/api/admin/destinations/${id}`, { token: adminToken })).status)
}
ok(removed.every((s) => s === 204), 'destinos de prueba borrados (ciudad, región y país)', removed.join('/'))
const destinationsLeft = items((await call('GET', '/api/destinations')).data)
ok(destinationsLeft.filter((d) => d.name.startsWith('QA')).length === 0, 'no quedan destinos QA en el catálogo', `${destinationsLeft.length} destinos`)
const publicNow = await call('GET', '/api/experiences?page=1&pageSize=1')
const packagesNow = await call('GET', '/api/packages?page=1&pageSize=1')
ok(publicNow.data?.totalCount === 90 && packagesNow.data?.totalCount === 13, 'catálogo público intacto (90 experiencias, 13 paquetes)', `${publicNow.data?.totalCount}/${packagesNow.data?.totalCount}`)

console.log(`\nRESULTADO: ${failures === 0 ? 'TODO OK' : failures + ' fallas'} (${checks} verificaciones)`)
process.exit(failures === 0 ? 0 : 1)
