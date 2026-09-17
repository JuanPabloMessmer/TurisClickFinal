// Resuelve la imagen representativa de cada destino en Wikimedia Commons y guarda
// destination-images.manifest.json (URL, autor, licencia, fuente) y ATTRIBUTIONS-DESTINOS.md.
// Destination sólo guarda ImageUrl: la atribución vive versionada acá.
//
// Uso: node resolve-destination-images.mjs [--refresh "La Paz,Sucre"] [--skip "Cobija,Camiri"]
import { readFile, writeFile } from 'node:fs/promises'
import { resolvePinned, searchFirst } from './commons.mjs'
import { DESTINATION_IMAGES } from './destinations.mjs'

const MANIFEST = new URL('./destination-images.manifest.json', import.meta.url)
const ATTRIBUTIONS = new URL('./ATTRIBUTIONS-DESTINOS.md', import.meta.url)

const argList = (flag) => new Set((process.argv.find((a, i) => process.argv[i - 1] === flag) ?? '').split(',').map((s) => s.trim()).filter(Boolean))
const refresh = argList('--refresh')
let manifest = { source: 'Wikimedia Commons', items: {}, rejected: {} }
try { manifest = { rejected: {}, ...JSON.parse(await readFile(MANIFEST, 'utf8')) } } catch { /* primera ejecución */ }
// --skip marca archivos revisados a mano como no representativos, para que no se vuelvan a elegir.
for (const name of argList('--skip')) {
  if (manifest.items[name]) {
    manifest.rejected[name] = [...(manifest.rejected[name] ?? []), manifest.items[name].file]
    delete manifest.items[name]
  }
}

for (const name of Object.keys(manifest.items)) if (!DESTINATION_IMAGES[name]) delete manifest.items[name]
// Cada destino con una foto distinta: una misma imagen en dos ciudades confundiría.
const used = new Set(Object.values(manifest.items).map((i) => i.file))
let missing = 0

for (const [name, spec] of Object.entries(DESTINATION_IMAGES)) {
  if (manifest.items[name] && !refresh.has(name)) continue
  if (manifest.items[name]) used.delete(manifest.items[name].file)
  const blocked = new Set([...used, ...(manifest.rejected[name] ?? [])])

  let entry = null
  if (spec.files?.length) entry = (await resolvePinned(spec.files)).find((e) => !blocked.has(e.file)) ?? null
  for (const q of spec.q ?? []) {
    if (entry) break
    entry = await searchFirst(q, blocked)
  }

  if (entry) {
    manifest.items[name] = entry
    used.add(entry.file)
  } else {
    delete manifest.items[name]
    missing++
  }
  console.log(`${entry ? 'OK   ' : 'FALTA'} ${name}: ${entry?.file.replace(/^File:/, '') ?? ''}`)
  await writeFile(MANIFEST, JSON.stringify(manifest, null, 2) + '\n')
}

const cell = (text) => text.replace(/\|/g, '/')
const lines = [
  '# Atribución de imágenes de destinos',
  '',
  'Imágenes de [Wikimedia Commons](https://commons.wikimedia.org/) bajo licencias libres (CC BY, CC BY-SA, CC0 o',
  'dominio público), enlazadas sin modificar en su miniatura de 1280 px. `Destination.ImageUrl` guarda solo la URL;',
  'autor, licencia y fuente quedan acá. Generado por `resolve-destination-images.mjs`.',
  '',
  '| Destino | Archivo | Autor | Licencia |',
  '|---|---|---|---|',
]
for (const name of Object.keys(DESTINATION_IMAGES)) {
  const i = manifest.items[name]
  if (!i) continue
  const lic = i.licenseUrl ? `[${i.license}](${i.licenseUrl})` : i.license
  lines.push(`| ${name} | [${cell(i.file.replace(/^File:/, ''))}](${i.pageUrl}) | ${cell(i.author)} | ${lic} |`)
}
await writeFile(ATTRIBUTIONS, lines.join('\n') + '\n')
console.log(`\ndestinos sin imagen: ${missing}`)
