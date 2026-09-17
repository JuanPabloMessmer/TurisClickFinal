// Resuelve las imágenes del catálogo en Wikimedia Commons y guarda images.manifest.json con autor y licencia.
// El modelo ExperienceImage/PackageImage sólo guarda url + isCover: la atribución vive en este manifiesto
// (y en ATTRIBUTIONS.md, que se genera desde él). Sólo licencias libres: CC BY, CC BY-SA, CC0 o dominio público.
//
// Uso: node resolve-images.mjs [--refresh key1,key2]   (sin --refresh sólo resuelve las que faltan)
import { readFile, writeFile } from 'node:fs/promises'
import { EXPERIENCES } from './catalog.mjs'
import { commons, IIPROPS, toEntry } from './commons.mjs'

const MANIFEST = new URL('./images.manifest.json', import.meta.url)
const ATTRIBUTIONS = new URL('./ATTRIBUTIONS.md', import.meta.url)
const PER_EXPERIENCE = 3

const refresh = new Set((process.argv.find((a, i) => process.argv[i - 1] === '--refresh') ?? '').split(',').filter(Boolean))
let manifest = { source: 'Wikimedia Commons', items: {} }
try { manifest = JSON.parse(await readFile(MANIFEST, 'utf8')) } catch { /* primera ejecución */ }

for (const key of Object.keys(manifest.items)) if (!EXPERIENCES.some((e) => e.key === key)) delete manifest.items[key]
const used = new Set(Object.values(manifest.items).flat().map((i) => i.file))
let missing = 0
for (const exp of EXPERIENCES) {
  if (manifest.items[exp.key]?.length && !refresh.has(exp.key)) continue
  for (const f of manifest.items[exp.key] ?? []) used.delete(f.file)
  const chosen = []

  if (exp.files?.length) {
    const data = await commons({ action: 'query', titles: exp.files.join('|'), ...IIPROPS })
    for (const title of exp.files) {
      const page = data.query?.pages?.find((p) => p.title === title.replace(/_/g, ' '))
      const entry = page && toEntry(page, { strict: false })
      if (entry) chosen.push(entry)
      else console.log(`  ! ${exp.key}: archivo fijado no válido o sin licencia libre: ${title}`)
    }
  }

  for (const strict of [true, false]) {
    for (const q of exp.img ?? []) {
      if (chosen.length >= PER_EXPERIENCE) break
      const data = await commons({ action: 'query', generator: 'search', gsrsearch: `${q} filetype:bitmap`, gsrnamespace: '6', gsrlimit: '15', ...IIPROPS })
      const pages = (data.query?.pages ?? []).sort((a, b) => a.index - b.index)
      for (const page of pages) {
        if (chosen.length >= PER_EXPERIENCE) break
        if (used.has(page.title) || chosen.some((c) => c.file === page.title)) continue
        const entry = toEntry(page, { strict })
        if (entry) chosen.push(entry)
      }
    }
    if (chosen.length >= 2 || !exp.img?.length) break
  }

  chosen.forEach((c) => used.add(c.file))
  manifest.items[exp.key] = chosen
  if (!chosen.length) missing++
  console.log(`${chosen.length ? 'OK  ' : 'FALTA'} ${exp.key}: ${chosen.map((c) => c.file.replace(/^File:/, '')).join(' | ')}`)
  await writeFile(MANIFEST, JSON.stringify(manifest, null, 2) + '\n') // guardado incremental
}

// ATTRIBUTIONS.md: créditos legibles para cada imagen usada.
const lines = ['# Atribución de imágenes del catálogo demo', '',
  'Todas las imágenes provienen de [Wikimedia Commons](https://commons.wikimedia.org/) bajo licencias libres.',
  'Se enlazan (hotlink) sin modificar desde `upload.wikimedia.org`, en su versión de 1280 px de ancho.',
  'Generado por `resolve-images.mjs` a partir de `images.manifest.json`.', '']
for (const exp of EXPERIENCES) {
  const imgs = manifest.items[exp.key] ?? []
  if (!imgs.length) continue
  lines.push(`## ${exp.title}`, '')
  for (const i of imgs) {
    const lic = i.licenseUrl ? `[${i.license}](${i.licenseUrl})` : i.license
    lines.push(`- [${i.file.replace(/^File:/, '')}](${i.pageUrl}) — ${i.author.replace(/\|/g, '/')} — ${lic}`)
  }
  lines.push('')
}
await writeFile(ATTRIBUTIONS, lines.join('\n'))
console.log(`\nexperiencias sin imagen: ${missing}`)
