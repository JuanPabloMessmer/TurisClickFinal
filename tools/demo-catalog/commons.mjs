// Acceso a Wikimedia Commons compartido por resolve-images.mjs (experiencias) y
// resolve-destination-images.mjs (destinos). Solo licencias libres: CC BY, CC BY-SA, CC0 o dominio público.

const COMMONS = 'https://commons.wikimedia.org/w/api.php'
export const UA = 'TurisClickDemoCatalog/1.0 (thesis demo; https://github.com/JuanPabloMessmer/TurisClickFinal)'
const WIDTH = 1280
const PAUSE_MS = 1500

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

export async function commons(params) {
  const url = `${COMMONS}?${new URLSearchParams({ format: 'json', formatversion: '2', origin: '*', ...params })}`
  for (let attempt = 1; ; attempt++) {
    await sleep(PAUSE_MS)
    const res = await fetch(url, { headers: { 'User-Agent': UA } })
    if (res.status === 429 || res.status >= 500) {
      if (attempt >= 5) throw new Error(`Commons ${res.status}`)
      await sleep(10_000 * attempt)
      continue
    }
    return res.json()
  }
}

export const IIPROPS = {
  prop: 'imageinfo',
  iiprop: 'url|size|mime|extmetadata',
  iiurlwidth: String(WIDTH),
  iiextmetadatafilter: 'LicenseShortName|LicenseUrl|Artist|Categories|Restrictions',
}
const FREE_LICENSE = /^(cc[ -]by(-sa)?([ -]\d(\.\d)?)?( [a-z]{2,})?|cc0( 1\.0)?|public domain|pd\b.*)$/i
const UNWANTED = /watermark|logo|\bmaps?\b|mapa|plano|coat of arms|escudo|flag|bandera|diagram|drawing|scan|stamp|sello|poster|afiche|screenshot|montage|collage/i

const strip = (html) => (html ?? '').replace(/<[^>]+>/g, '').replace(/\s+/g, ' ').trim()

/** Entrada del manifiesto si la imagen es usable (JPEG, licencia libre, sin marcas, apaisada), o null. */
export function toEntry(page, { strict }) {
  const info = page.imageinfo?.[0]
  if (!info || info.mime !== 'image/jpeg') return null
  const meta = info.extmetadata ?? {}
  const license = strip(meta.LicenseShortName?.value)
  if (!FREE_LICENSE.test(license)) return null
  if (UNWANTED.test(page.title) || UNWANTED.test(meta.Categories?.value ?? '')) return null
  if (strip(meta.Restrictions?.value)) return null // p. ej. trademarked / personality rights
  const ratio = info.width / info.height
  if (strict && (info.width < 1000 || ratio < 1.2 || ratio > 2.2)) return null
  if (!strict && (info.width < 800 || ratio < 1)) return null
  const url = info.thumburl ?? info.url
  if (url.length > 500) return null
  return {
    file: page.title,
    url,
    pageUrl: info.descriptionurl,
    author: strip(meta.Artist?.value) || 'Desconocido',
    license,
    licenseUrl: meta.LicenseUrl?.value ?? null,
  }
}

/** Archivos fijados a mano, en orden, descartando los que no pasen el filtro de licencia. */
export async function resolvePinned(files) {
  const data = await commons({ action: 'query', titles: files.join('|'), ...IIPROPS })
  const out = []
  for (const title of files) {
    const page = data.query?.pages?.find((p) => p.title === title.replace(/_/g, ' '))
    const entry = page && toEntry(page, { strict: false })
    if (entry) out.push(entry)
    else console.log(`  ! archivo fijado no válido o sin licencia libre: ${title}`)
  }
  return out
}

/** Primera imagen usable de una búsqueda (en el orden de relevancia de Commons) que no esté en `used`. */
export async function searchFirst(query, used) {
  for (const strict of [true, false]) {
    const data = await commons({ action: 'query', generator: 'search', gsrsearch: `${query} filetype:bitmap`, gsrnamespace: '6', gsrlimit: '15', ...IIPROPS })
    const pages = (data.query?.pages ?? []).sort((a, b) => a.index - b.index)
    for (const page of pages) {
      if (used.has(page.title)) continue
      const entry = toEntry(page, { strict })
      if (entry) return entry
    }
  }
  return null
}
