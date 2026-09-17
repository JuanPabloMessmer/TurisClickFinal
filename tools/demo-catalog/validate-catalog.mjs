// Validación automatizada del catálogo demo contra la API REAL (por defecto, Azure V2). Sólo lecturas:
// no crea, modifica ni reserva nada. Nunca imprime contraseñas ni tokens.
//
// Uso: node validate-catalog.mjs [--skip-images]
import { readFile } from 'node:fs/promises'
import { CAT, EXPERIENCES, LEGACY, NEW_CITIES, PACKAGES, PROVIDERS, SCHEDULE } from './catalog.mjs'

const API = process.env.TURISCLICK_API ?? 'https://app-turisclick-v2-api.azurewebsites.net'
const SKIP_IMAGES = process.argv.includes('--skip-images')
const manifest = JSON.parse(await readFile(new URL('./images.manifest.json', import.meta.url), 'utf8'))
const UA = 'TurisClickDemoCatalog/1.0 (thesis demo; https://github.com/JuanPabloMessmer/TurisClickFinal)'
const TODAY = new Date().toISOString().slice(0, 10)
const MIN_EXPERIENCE_DATES = 12
const MIN_PACKAGE_DEPARTURES = 5

let failures = 0
let checks = 0
const ok = (cond, label, extra = '') => {
  checks++
  if (!cond) failures++
  if (!cond || !label.startsWith('·')) console.log(`${cond ? 'OK  ' : 'FAIL'} ${label}${extra ? ' — ' + extra : ''}`)
  return cond
}
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
async function allPublic(path, token) {
  const out = []
  for (let page = 1; ; page++) {
    const r = await call('GET', `${path}${path.includes('?') ? '&' : '?'}page=${page}&pageSize=100`, { token })
    if (r.status !== 200) throw new Error(`GET ${path} -> ${r.status}`)
    out.push(...items(r.data))
    if (items(r.data).length < 100) return out
  }
}
const count = (list, fn) => list.reduce((m, x) => { const k = fn(x); m[k] = (m[k] ?? 0) + 1; return m }, {})

// ───────────────────────── estructura ─────────────────────────
console.log('===== ESTRUCTURA =====')
const categories = items((await call('GET', '/api/categories')).data)
ok(categories.length === 6 && Object.values(CAT).every((n) => categories.some((c) => c.name === n)), 'categorías del seed intactas (6)')
const destinations = items((await call('GET', '/api/destinations')).data)
const cities = destinations.filter((d) => d.type === 'CITY')
ok(destinations.filter((d) => d.type === 'COUNTRY').length === 1 && destinations.filter((d) => d.type === 'REGION').length === 9, 'país y 9 departamentos intactos')
ok(cities.length === 42 + NEW_CITIES.length, `ciudades: 42 del seed + ${NEW_CITIES.length} nuevas`, `${cities.length}`)
for (const c of NEW_CITIES) {
  const d = cities.find((x) => x.name === c.name)
  ok(d?.parentName === c.region, `ciudad nueva ${c.name} bajo ${c.region}`, d?.parentName ?? 'no existe')
}
const cityByName = Object.fromEntries(cities.map((c) => [c.name, c]))

const destinationImages = JSON.parse(await readFile(new URL('./destination-images.manifest.json', import.meta.url), 'utf8')).items
const wrongImages = Object.entries(destinationImages).filter(([name, image]) => cityByName[name]?.imageUrl !== image.url).map(([name]) => name)
ok(wrongImages.length === 0, `imágenes de destinos aplicadas (${Object.keys(destinationImages).length})`, wrongImages.join(', '))
const catalogCitiesWithoutImage = [...new Set(EXPERIENCES.map((e) => e.city))].filter((name) => !cityByName[name]?.imageUrl)
ok(catalogCitiesWithoutImage.length === 0, 'toda ciudad con experiencias tiene imagen', catalogCitiesWithoutImage.join(', '))

// ───────────────────────── experiencias públicas ─────────────────────────
console.log('\n===== EXPERIENCIAS PÚBLICAS =====')
const publicExps = await allPublic('/api/experiences')
const byTitle = Object.fromEntries(publicExps.map((e) => [e.title, e]))
ok(publicExps.length === EXPERIENCES.length, `total publicado = catálogo (${EXPERIENCES.length})`, `${publicExps.length}`)
const missingTitles = EXPERIENCES.filter((e) => !byTitle[e.title]).map((e) => e.key)
ok(missingTitles.length === 0, 'todas las experiencias del catálogo están publicadas', missingTitles.join(', '))
for (const x of LEGACY.unpublishExperiences) ok(!byTitle[x.title], `legado "${x.title}" fuera del catálogo público`)
ok(publicExps.every((e) => e.status === 'PUBLISHED' && e.currency === 'BOB' && e.price > 0 && e.coverImageUrl), 'resúmenes: PUBLISHED, BOB, precio > 0 y portada')

const expectedByCity = count(EXPERIENCES, (e) => e.city)
for (const [city, expected] of Object.entries(expectedByCity)) {
  const d = cityByName[city]
  const filtered = await allPublic(`/api/experiences?destinationId=${d.id}`)
  ok(filtered.length === expected && d.publishedExperienceCount === expected, `· filtro ciudad ${city}`, `${filtered.length}/${expected}, publishedExperienceCount=${d.publishedExperienceCount}`)
}
console.log(`OK  filtro por ciudad revisado en ${Object.keys(expectedByCity).length} ciudades`)

for (const [k, name] of Object.entries(CAT)) {
  const c = categories.find((x) => x.name === name)
  const filtered = await allPublic(`/api/experiences?categoryId=${c.id}`)
  const expected = EXPERIENCES.filter((e) => e.cats.includes(k))
  ok(filtered.length === expected.length && expected.every((e) => filtered.some((f) => f.title === e.title)), `filtro categoría ${name}`, `${filtered.length}/${expected.length}`)
}
const combo = EXPERIENCES.filter((e) => e.city === 'La Paz' && e.cats.includes('AV'))
const comboRes = await allPublic(`/api/experiences?destinationId=${cityByName['La Paz'].id}&categoryId=${categories.find((c) => c.name === CAT.AV).id}`)
ok(comboRes.length === combo.length, 'filtro combinado La Paz + Aventura', `${comboRes.length}/${combo.length}`)

// ───────────────────────── detalle, imágenes y disponibilidad ─────────────────────────
console.log('\n===== DETALLE, IMÁGENES Y DISPONIBILIDAD =====')
const providerByKey = Object.fromEntries(PROVIDERS.map((p) => [p.key, p]))
const details = {}
const imageUrls = new Set(Object.values(destinationImages).map((i) => i.url))
let totalDates = 0
let minDates = Infinity
let lastDate = ''
for (const e of EXPERIENCES) {
  const pub = byTitle[e.title]
  if (!pub) continue
  const r = await call('GET', `/api/experiences/${pub.id}`)
  const d = r.data
  details[e.key] = d
  const manifestUrls = manifest.items[e.key].map((i) => i.url)
  ok(r.status === 200, `· detalle ${e.key}`)
  ok(d.destinationName === e.city, `· destino ${e.key}`, d.destinationName)
  ok(d.companyName === providerByKey[e.provider].companyName, `· proveedor ${e.key}`, d.companyName)
  ok(d.categories.length === e.cats.length && e.cats.every((c) => d.categories.some((x) => x.name === CAT[c])), `· categorías ${e.key}`)
  ok(d.price === e.price && d.currency === 'BOB' && d.durationMinutes === e.minutes, `· precio y duración ${e.key}`)
  ok(d.images.length >= 1 && d.images.filter((i) => i.isCover).length === 1 && d.images.every((i) => manifestUrls.includes(i.url)), `· imágenes ${e.key}`, `${d.images.length}`)
  d.images.forEach((i) => imageUrls.add(i.url))

  const slots = items((await call('GET', `/api/experiences/${pub.id}/availability`)).data)
  const future = slots.filter((s) => s.date >= TODAY && s.status === 'OPEN' && s.availableSlots > 0)
  ok(future.length >= MIN_EXPERIENCE_DATES, `· disponibilidad ${e.key}`, `${future.length} fechas`)
  if (e.weekdays) {
    const wrong = future.filter((s) => s.startTime.startsWith(e.start.slice(0, 5)) && !e.weekdays.includes(new Date(`${s.date}T00:00:00Z`).getUTCDay()))
    ok(wrong.length === 0, `· días de operación ${e.key}`, wrong.map((s) => s.date).join(','))
  }
  totalDates += future.length
  minDates = Math.min(minDates, future.length)
  lastDate = future.map((s) => s.date).sort().at(-1) > lastDate ? future.map((s) => s.date).sort().at(-1) : lastDate
}
console.log(`OK  detalle de ${Object.keys(details).length} experiencias: destino, proveedor, categorías, precio, portada`)
ok(minDates >= MIN_EXPERIENCE_DATES, 'todas las experiencias con fechas futuras abiertas', `mín=${minDates}, total=${totalDates}, última=${lastDate}`)
ok(lastDate >= SCHEDULE.until.slice(0, 7), 'el calendario llega hasta el final del período demo', lastDate)

// ───────────────────────── paquetes ─────────────────────────
console.log('\n===== PAQUETES =====')
const publicPkgs = await allPublic('/api/packages')
ok(publicPkgs.length === PACKAGES.length, `total paquetes publicados = catálogo (${PACKAGES.length})`, `${publicPkgs.length}`)
const expById = Object.fromEntries(Object.values(details).map((d) => [d.id, d]))
for (const k of PACKAGES) {
  const pub = publicPkgs.find((p) => p.title === k.title)
  if (!pub) { ok(false, `paquete publicado ${k.key}`); continue }
  const d = (await call('GET', `/api/packages/${pub.id}`)).data
  const refs = d.items.filter((i) => i.kind === 'EXPERIENCE_REFERENCE')
  ok(d.items.length === k.items.length && d.durationDays === k.days && d.price === k.price, `· ítems/duración/precio ${k.key}`, `${d.items.length} ítems`)
  ok(d.companyName === providerByKey[k.provider].companyName, `· proveedor ${k.key}`)
  ok(refs.every((i) => expById[i.experienceId]?.companyId === d.companyId), `· ownership de referencias ${k.key}`)
  ok(d.images.length >= 1 && d.images.filter((i) => i.isCover).length === 1, `· imágenes ${k.key}`)
  d.images.forEach((i) => imageUrls.add(i.url))
  const deps = items((await call('GET', `/api/packages/${pub.id}/availability`)).data).filter((a) => a.departureDate >= TODAY && a.status === 'OPEN')
  ok(deps.length >= MIN_PACKAGE_DEPARTURES, `· salidas ${k.key}`, `${deps.length}`)
  if (k.weekdays) ok(deps.every((a) => k.weekdays.includes(new Date(`${a.departureDate}T00:00:00Z`).getUTCDay())), `· día de salida ${k.key}`)
}
console.log(`OK  ${PACKAGES.length} paquetes: ítems, proveedor, ownership de referencias, imágenes y salidas`)

// ───────────────────────── imágenes accesibles ─────────────────────────
if (!SKIP_IMAGES) {
  console.log('\n===== IMÁGENES =====')
  const broken = []
  for (const url of imageUrls) {
    let res = await fetch(url, { method: 'HEAD', headers: { 'User-Agent': UA } })
    if (res.status === 429) { await sleep(8000); res = await fetch(url, { method: 'HEAD', headers: { 'User-Agent': UA } }) }
    if (res.status !== 200 || !(res.headers.get('content-type') ?? '').startsWith('image/')) broken.push(`${res.status} ${url}`)
    await sleep(250)
  }
  ok(broken.length === 0, `imágenes accesibles (${imageUrls.size} URLs únicas)`, broken.slice(0, 5).join(' ; '))
  const allManifest = Object.values(manifest.items).flat()
  ok(allManifest.every((i) => i.author && i.license && i.pageUrl), 'manifiesto: cada imagen con autor, licencia y página de origen')
}

// ───────────────────────── ownership por proveedor ─────────────────────────
console.log('\n===== PROVEEDORES =====')
const providerSessions = {}
for (const p of PROVIDERS) {
  const password = process.env[p.passwordEnv]
  if (!password) { ok(false, `contraseña de ${p.email} disponible`, `falta ${p.passwordEnv}`); continue }
  const login = await call('POST', '/api/auth/login', { body: { email: p.email, password } })
  ok(login.status === 200 && login.data.user.role === 'PROVIDER', `login proveedor ${p.companyName}`)
  if (login.status !== 200) continue
  providerSessions[p.key] = login.data.accessToken
  const mine = await allPublic('/api/experiences/mine', login.data.accessToken)
  const expected = EXPERIENCES.filter((e) => e.provider === p.key).map((e) => e.title)
  const legacy = LEGACY.unpublishExperiences.filter((x) => x.provider === p.key).map((x) => x.title)
  const extra = mine.filter((m) => !expected.includes(m.title) && !legacy.includes(m.title))
  ok(expected.every((t) => mine.some((m) => m.title === t)) && extra.length === 0, `· ${p.companyName}: experiencias propias`, `${mine.length}`)
  ok(mine.filter((m) => m.status === 'PUBLISHED').every((m) => p.zone.includes(m.destinationName)), `${p.companyName}: opera sólo en su zona`, [...new Set(mine.map((m) => m.destinationName))].join(', '))
}
const [a, b] = PROVIDERS
const foreign = details[EXPERIENCES.find((e) => e.provider === b.key).key]
if (providerSessions[a.key] && foreign) {
  const r = await call('GET', `/api/experiences/mine/${foreign.id}`, { token: providerSessions[a.key] })
  ok([403, 404].includes(r.status), 'un proveedor no puede ver como propia una experiencia ajena', `${r.status}`)
}

// ───────────────────────── cuentas demo ─────────────────────────
console.log('\n===== CUENTAS DEMO =====')
const t = LEGACY.demoTourist
const tl = await call('POST', '/api/auth/login', { body: { email: t.email, password: process.env[t.passwordEnv] } })
ok(tl.status === 200 && tl.data.user.role === 'TOURIST', `${t.email}: login OK, rol TOURIST (cuenta activa)`)
if (tl.status === 200) {
  const r = await call('GET', '/api/reservations/me?page=1&pageSize=20', { token: tl.data.accessToken })
  ok(r.status === 200 && items(r.data).length === 0, `${t.email}: sin reservas`, `${items(r.data).length}`)
}
const admin = await call('POST', '/api/auth/login', { body: { email: 'admin@turisclick.dev', password: process.env.ADMIN_PASSWORD } })
if (ok(admin.status === 200, 'login admin (para revisar estados de cuenta)')) {
  const status = async (email) => items((await call('GET', `/api/admin/users?search=${encodeURIComponent(email)}&pageSize=20`, { token: admin.data.accessToken })).data).find((u) => u.email === email)?.status
  ok((await status(t.email)) === 'ACTIVE', `${t.email}: ACTIVE`)
  for (const email of LEGACY.suspendUsers) ok((await status(email)) === 'SUSPENDED', `${email}: SUSPENDED (cuenta QA retirada)`)
}

console.log(`\nRESULTADO: ${failures === 0 ? 'TODO OK' : failures + ' fallas'} (${checks} verificaciones)`)
process.exit(failures === 0 ? 0 : 1)
