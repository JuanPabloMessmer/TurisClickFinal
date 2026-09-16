// Carga el catálogo demo en TurisClick V2 usando SÓLO la API pública/autenticada, como lo haría un usuario real.
// Idempotente: reutiliza destinos, cuentas, empresas, experiencias, fechas y paquetes que ya existan
// (coincidencia por nombre/título dentro de cada proveedor). Nunca borra nada: la API no tiene DELETE de
// contenido comercial; lo que se retira se despublica o se suspende.
//
// Contraseñas por variables de entorno (ver run-catalog.ps1). Nunca se imprimen contraseñas ni tokens.
//
// Uso: node load-catalog.mjs [--check]     --check valida el catálogo sin llamar a la API
import { readFile } from 'node:fs/promises'
import { CAT, EXPERIENCES, LEGACY, NEW_CITIES, PACKAGES, PROVIDERS, SCHEDULE } from './catalog.mjs'

const API = process.env.TURISCLICK_API ?? 'https://app-turisclick-v2-api.azurewebsites.net'
const CHECK_ONLY = process.argv.includes('--check')
const manifest = JSON.parse(await readFile(new URL('./images.manifest.json', import.meta.url), 'utf8'))

// ───────────────────────── validación offline del catálogo ─────────────────────────
const providerByKey = Object.fromEntries(PROVIDERS.map((p) => [p.key, p]))
const expByKey = Object.fromEntries(EXPERIENCES.map((e) => [e.key, e]))
const problems = []
const dup = (list, field, what) => {
  const seen = new Set()
  for (const x of list) { if (seen.has(x[field])) problems.push(`${what} duplicado: ${x[field]}`); seen.add(x[field]) }
}
dup(EXPERIENCES, 'key', 'key de experiencia'); dup(EXPERIENCES, 'title', 'título de experiencia')
dup(PACKAGES, 'key', 'key de paquete'); dup(PACKAGES, 'title', 'título de paquete')
for (const e of EXPERIENCES) {
  const p = providerByKey[e.provider]
  if (!p) problems.push(`${e.key}: proveedor inexistente ${e.provider}`)
  else if (!p.zone.includes(e.city)) problems.push(`${e.key}: ${e.city} está fuera de la zona de ${p.companyName}`)
  if (!e.cats.every((c) => CAT[c])) problems.push(`${e.key}: categoría desconocida`)
  if (!manifest.items[e.key]?.length) problems.push(`${e.key}: sin imágenes en images.manifest.json`)
  if (e.description.length < 10 || e.title.length > 200) problems.push(`${e.key}: título/descripción fuera de rango`)
}
for (const k of PACKAGES) {
  const p = providerByKey[k.provider]
  if (!p?.zone.includes(k.city)) problems.push(`${k.key}: ${k.city} fuera de la zona del proveedor`)
  for (const it of k.items) {
    if (it.e && expByKey[it.e]?.provider !== k.provider) problems.push(`${k.key}: referencia ${it.e} de otro proveedor o inexistente`)
  }
  if (!k.imgFrom.every((x) => manifest.items[x]?.length)) problems.push(`${k.key}: imágenes de origen sin resolver`)
}
if (problems.length) {
  console.log('CATÁLOGO INVÁLIDO:\n- ' + problems.join('\n- '))
  process.exit(1)
}
console.log(`catálogo OK: ${PROVIDERS.length} proveedores, ${EXPERIENCES.length} experiencias, ${PACKAGES.length} paquetes`)
if (CHECK_ONLY) process.exit(0)

// ───────────────────────── fechas ─────────────────────────
const DAY = 86_400_000
const iso = (d) => d.toISOString().slice(0, 10)
const parse = (s) => new Date(`${s}T00:00:00Z`)
const hash = (s) => [...s].reduce((h, c) => (h * 31 + c.charCodeAt(0)) >>> 0, 7)
function experienceDates(e) {
  const allowed = e.weekdays ?? [0, 1, 2, 3, 4, 5, 6]
  const preferred = allowed[hash(e.key) % allowed.length]
  let d = parse(SCHEDULE.from)
  while (d.getUTCDay() !== preferred) d = new Date(d.getTime() + DAY)
  const out = []
  for (; d <= parse(SCHEDULE.until); d = new Date(d.getTime() + (d <= parse(SCHEDULE.weeklyUntil) ? 7 : 14) * DAY)) out.push(iso(d))
  return out
}
function packageDates(k, index) {
  let d = new Date(parse(SCHEDULE.from).getTime() + (7 + index * 2) * DAY)
  if (k.weekdays) while (!k.weekdays.includes(d.getUTCDay())) d = new Date(d.getTime() + DAY)
  const out = []
  for (; d <= parse(SCHEDULE.until); d = new Date(d.getTime() + SCHEDULE.packageEveryDays * DAY)) out.push(iso(d))
  return out
}

// ───────────────────────── HTTP ─────────────────────────
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
const must = (r, what, ok = [200, 201, 204]) => {
  if (!ok.includes(r.status)) throw new Error(`${what} -> ${r.status} ${JSON.stringify(r.data?.detail ?? r.data?.title ?? r.data?.errors ?? '').slice(0, 300)}`)
  return r.data
}
const items = (d) => (Array.isArray(d) ? d : d?.items ?? [])

/** Sesión que se re-autentica sola si el access token expira durante la carga. */
async function session(email, password) {
  let auth = must(await call('POST', '/api/auth/login', { body: { email, password } }), `login ${email}`)
  const s = {
    get user() { return auth.user },
    async relogin() { auth = must(await call('POST', '/api/auth/login', { body: { email, password } }), `login ${email}`) },
    async call(method, path, body) {
      let r = await call(method, path, { body, token: auth.accessToken })
      if (r.status === 401) { await s.relogin(); r = await call(method, path, { body, token: auth.accessToken }) }
      return r
    },
  }
  return s
}
async function all(s, path) {
  const out = []
  for (let page = 1; ; page++) {
    const d = must(await s.call('GET', `${path}${path.includes('?') ? '&' : '?'}page=${page}&pageSize=100`), `GET ${path}`)
    out.push(...items(d))
    if (items(d).length < 100) return out
  }
}
const env = (name) => {
  if (!process.env[name]) throw new Error(`falta la variable de entorno ${name}`)
  return process.env[name]
}

// ───────────────────────── 1. destinos y categorías ─────────────────────────
const admin = await session('admin@turisclick.dev', env('ADMIN_PASSWORD'))
const categories = items(must(await call('GET', '/api/categories'), 'categorías'))
const catId = Object.fromEntries(Object.entries(CAT).map(([k, name]) => {
  const c = categories.find((x) => x.name === name)
  if (!c) throw new Error(`no existe la categoría ${name}`)
  return [k, c.id]
}))

let destinations = items(must(await call('GET', '/api/destinations'), 'destinos'))
for (const city of NEW_CITIES) {
  if (destinations.some((d) => d.type === 'CITY' && d.name === city.name)) { console.log(`destino ${city.name}: ya existía`); continue }
  const region = destinations.find((d) => d.type === 'REGION' && d.name === city.region)
  if (!region) throw new Error(`no existe la región ${city.region}`)
  must(await admin.call('POST', '/api/admin/destinations', { name: city.name, type: 'CITY', parentId: region.id }), `crear destino ${city.name}`)
  console.log(`destino ${city.name}: creado en ${city.region}`)
}
destinations = items(must(await call('GET', '/api/destinations'), 'destinos'))
const cityId = (name) => {
  const d = destinations.find((x) => x.type === 'CITY' && x.name === name)
  if (!d) throw new Error(`no existe la ciudad ${name}`)
  return d.id
}

// ───────────────────────── 2. proveedores ─────────────────────────
const sessions = {}
for (const p of PROVIDERS) {
  const password = env(p.passwordEnv)
  const reg = await call('POST', '/api/providers/register', {
    body: {
      firstName: p.firstName, lastName: p.lastName, email: p.email, password,
      companyName: p.companyName, companyDescription: p.description, legalDocument: p.legalDocument, contactEmail: p.contactEmail,
    },
  })
  if (![200, 201, 409].includes(reg.status)) must(reg, `registro ${p.email}`)

  let s = await session(p.email, password)
  const company = must(await admin.call('GET', `/api/admin/companies/${s.user.companyId}`), `empresa de ${p.email}`)
  if (company.status === 'SUSPENDED') must(await admin.call('POST', `/api/admin/companies/${company.id}/reactivate`), 'reactivar empresa')
  else if (company.status !== 'APPROVED') must(await admin.call('POST', `/api/admin/companies/${company.id}/approve`), 'aprobar empresa')
  await s.relogin() // el token nuevo refleja la empresa aprobada

  const mine = must(await s.call('GET', '/api/companies/me'), 'mi empresa')
  if (mine.name !== p.companyName || mine.description !== p.description || mine.contactEmail !== p.contactEmail) {
    must(await s.call('PUT', '/api/companies/me', { name: p.companyName, description: p.description, contactEmail: p.contactEmail, contactPhone: mine.contactPhone ?? null }), 'actualizar empresa')
  }
  sessions[p.key] = s
  console.log(`proveedor ${p.companyName}: ${reg.status === 409 ? 'existente' : 'registrado'}, empresa ${company.status === 'APPROVED' ? 'ya aprobada' : 'aprobada ahora'}`)
}

// ───────────────────────── 3. experiencias ─────────────────────────
const imagesOf = (keys, max) => keys.flatMap((k) => manifest.items[k]).slice(0, max).map((i, n) => ({ url: i.url, isCover: n === 0 }))
const expIds = {}
const totals = { created: 0, updated: 0, dates: 0, published: 0 }
for (const p of PROVIDERS) {
  const s = sessions[p.key]
  const mine = await all(s, '/api/experiences/mine')
  for (const e of EXPERIENCES.filter((x) => x.provider === p.key)) {
    const body = {
      title: e.title, description: e.description, destinationId: cityId(e.city), categoryIds: e.cats.map((c) => catId[c]),
      includesText: e.includes, excludesText: e.excludes, durationMinutes: e.minutes, durationLabel: e.label,
      price: e.price, currency: 'BOB', images: imagesOf([e.key], 3),
    }
    let exp = mine.find((m) => m.title === e.title)
    if (exp) { must(await s.call('PUT', `/api/experiences/${exp.id}`, body), `actualizar ${e.key}`); totals.updated++ }
    else { exp = must(await s.call('POST', '/api/experiences', body), `crear ${e.key}`); totals.created++ }
    expIds[e.key] = exp.id

    const existing = new Set(items(must(await s.call('GET', `/api/experiences/mine/${exp.id}/availability`), `fechas ${e.key}`)).map((a) => a.date))
    let added = 0
    for (const date of experienceDates(e)) {
      if (existing.has(date)) continue
      must(await s.call('POST', `/api/experiences/${exp.id}/availability`, { date, startTime: e.start, totalSlots: e.slots }), `fecha ${e.key} ${date}`, [200, 201, 409])
      added++
    }
    totals.dates += added
    const status = must(await s.call('GET', `/api/experiences/mine/${exp.id}`), `estado ${e.key}`).status
    if (status !== 'PUBLISHED') { must(await s.call('POST', `/api/experiences/${exp.id}/publish`), `publicar ${e.key}`); totals.published++ }
    console.log(`  ${e.key}: fechas nuevas=${added}${status !== 'PUBLISHED' ? ' publicada' : ''}`)
  }
}
console.log(`experiencias: creadas=${totals.created} actualizadas=${totals.updated} fechas_nuevas=${totals.dates} publicadas_ahora=${totals.published}`)

// ───────────────────────── 4. paquetes ─────────────────────────
for (const [index, k] of PACKAGES.entries()) {
  const s = sessions[k.provider]
  const mine = await all(s, '/api/packages/mine')
  const sortByDay = {}
  const body = {
    title: k.title, description: k.description, destinationId: cityId(k.city), categoryIds: k.cats.map((c) => catId[c]),
    conditionsText: k.conditions, durationDays: k.days, price: k.price, currency: 'BOB',
    items: k.items.map((it) => {
      const sortOrder = (sortByDay[it.day] = (sortByDay[it.day] ?? 0) + 1)
      return it.e
        ? { dayNumber: it.day, sortOrder, kind: 'EXPERIENCE_REFERENCE', experienceId: expIds[it.e] }
        : { dayNumber: it.day, sortOrder, kind: 'DESCRIPTIVE', title: it.d[0], description: it.d[1] }
    }),
    images: imagesOf(k.imgFrom, 3),
  }
  let pkg = mine.find((m) => m.title === k.title)
  if (pkg) must(await s.call('PUT', `/api/packages/${pkg.id}`, body), `actualizar ${k.key}`)
  else pkg = must(await s.call('POST', '/api/packages', body), `crear ${k.key}`)

  const existing = new Set(items(must(await s.call('GET', `/api/packages/mine/${pkg.id}/availability`), `salidas ${k.key}`)).map((a) => a.departureDate))
  let added = 0
  for (const departureDate of packageDates(k, index)) {
    if (existing.has(departureDate)) continue
    must(await s.call('POST', `/api/packages/${pkg.id}/availability`, { departureDate, totalSlots: k.slots }), `salida ${k.key} ${departureDate}`, [200, 201, 409])
    added++
  }
  const status = must(await s.call('GET', `/api/packages/mine/${pkg.id}`), `estado ${k.key}`).status
  if (status !== 'PUBLISHED') must(await s.call('POST', `/api/packages/${pkg.id}/publish`), `publicar ${k.key}`)
  console.log(`paquete ${k.key}: ${mine.some((m) => m.title === k.title) ? 'actualizado' : 'creado'}, salidas nuevas=${added}`)
}

// ───────────────────────── 5. limpieza de datos demo/QA previos (sin borrar) ─────────────────────────
for (const x of LEGACY.unpublishExperiences) {
  const s = sessions[x.provider]
  const exp = (await all(s, '/api/experiences/mine')).find((m) => m.title === x.title)
  if (!exp) { console.log(`legado "${x.title}": no existe`); continue }
  const status = must(await s.call('GET', `/api/experiences/mine/${exp.id}`), 'estado legado').status
  if (status === 'PUBLISHED') must(await s.call('POST', `/api/experiences/${exp.id}/unpublish`), `despublicar ${x.title}`)
  console.log(`legado "${x.title}": ${status === 'PUBLISHED' ? 'despublicada ahora' : `ya estaba ${status}`}`)
}
const findUser = async (email) => items(must(await admin.call('GET', `/api/admin/users?search=${encodeURIComponent(email)}&pageSize=20`), `buscar ${email}`)).find((u) => u.email === email)
for (const email of LEGACY.suspendUsers) {
  const u = await findUser(email)
  if (!u) { console.log(`usuario QA ${email}: no existe`); continue }
  if (u.status !== 'SUSPENDED') must(await admin.call('POST', `/api/admin/users/${u.id}/suspend`), `suspender ${email}`)
  console.log(`usuario QA ${email}: ${u.status !== 'SUSPENDED' ? 'suspendido ahora' : 'ya suspendido'}`)
}

// ───────────────────────── 6. turista demo limpio ─────────────────────────
const t = LEGACY.demoTourist
const treg = await call('POST', '/api/auth/register', { body: { firstName: t.firstName, lastName: t.lastName, email: t.email, password: env(t.passwordEnv) } })
if (![200, 201, 409].includes(treg.status)) must(treg, 'registro turista demo')
let tu = await findUser(t.email)
if (tu.status !== 'ACTIVE') { must(await admin.call('POST', `/api/admin/users/${tu.id}/activate`), 'activar turista demo'); tu = await findUser(t.email) }
const tourist = await session(t.email, env(t.passwordEnv))
const reservations = items(must(await tourist.call('GET', '/api/reservations/me?page=1&pageSize=20'), 'reservas turista demo'))
console.log(`turista demo: estado=${tu.status} reservas=${reservations.length}`)
if (reservations.length) console.log('  ATENCIÓN: la cuenta demo tiene reservas; no se tocan automáticamente.')

const pub = must(await call('GET', '/api/experiences?page=1&pageSize=1'), 'catálogo')
const pubPkg = must(await call('GET', '/api/packages?page=1&pageSize=1'), 'paquetes')
console.log(`\ncatálogo público: experiencias=${pub.totalCount} paquetes=${pubPkg.totalCount}`)
