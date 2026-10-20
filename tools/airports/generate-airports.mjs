#!/usr/bin/env node
/**
 * Genera `src/TurisClick.Api/Modules/Flights/Data/airports.json` a partir del dataset de OurAirports
 * (https://ourairports.com/data/, dominio público).
 *
 * Uso (el CSV se descarga a mano y se pasa por argumento; este script no baja nada):
 *
 *   curl -sSfL -o airports.csv https://davidmegginson.github.io/ourairports-data/airports.csv
 *   node tools/airports/generate-airports.mjs airports.csv src/TurisClick.Api/Modules/Flights/Data/airports.json
 *
 * Qué entra: aeropuertos con código IATA de tres letras **y servicio regular de pasajeros**
 * (`scheduled_service = yes`). Es el criterio que usan los buscadores de vuelos: un aeródromo sin vuelos
 * comerciales no es un origen que alguien pueda elegir, y sumarlos solo llena el autocompletado de ruido.
 *
 * Los nombres de país salen en español de la ICU de Node (`Intl.DisplayNames`), no de una tabla escrita a
 * mano. Los nombres de ciudad quedan como los publica OurAirports; las traducciones que el producto
 * necesita ("Londres", "Nueva York") viven en AirportCatalog, no acá.
 */
import { readFileSync, writeFileSync } from 'node:fs'

const [input, output] = process.argv.slice(2)
if (!input || !output) {
  console.error('Uso: node generate-airports.mjs <airports.csv> <airports.json>')
  process.exit(1)
}

/** CSV de OurAirports: comas, comillas dobles y comillas escapadas duplicándolas. */
function parseCsv(text) {
  const rows = []
  let row = []
  let field = ''
  let quoted = false

  for (let i = 0; i < text.length; i++) {
    const ch = text[i]
    if (quoted) {
      if (ch === '"' && text[i + 1] === '"') {
        field += '"'
        i++
      } else if (ch === '"') {
        quoted = false
      } else {
        field += ch
      }
    } else if (ch === '"') {
      quoted = true
    } else if (ch === ',') {
      row.push(field)
      field = ''
    } else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && text[i + 1] === '\n') i++
      row.push(field)
      rows.push(row)
      row = []
      field = ''
    } else {
      field += ch
    }
  }
  if (field || row.length) {
    row.push(field)
    rows.push(row)
  }
  return rows
}

const [header, ...records] = parseCsv(readFileSync(input, 'utf8'))
const col = Object.fromEntries(header.map((name, index) => [name, index]))

const SIZES = { large_airport: 'L', medium_airport: 'M', small_airport: 'S' }
const countryNames = new Intl.DisplayNames(['es'], { type: 'region' })

const airports = new Map()
for (const r of records) {
  const iata = (r[col.iata_code] ?? '').trim().toUpperCase()
  const size = SIZES[r[col.type]]
  if (!/^[A-Z]{3}$/.test(iata) || !size || r[col.scheduled_service] !== 'yes') continue

  const candidate = {
    iata,
    name: r[col.name].trim(),
    city: (r[col.municipality] ?? '').trim(),
    countryCode: r[col.iso_country].trim().toUpperCase(),
    lat: Math.round(Number(r[col.latitude_deg]) * 100) / 100,
    lon: Math.round(Number(r[col.longitude_deg]) * 100) / 100,
    size,
  }

  // Un IATA repetido (pasa con aeródromos cerrados que conservan el código) se resuelve a favor del más
  // grande: es el que tiene los vuelos.
  const existing = airports.get(iata)
  if (!existing || 'LMS'.indexOf(size) < 'LMS'.indexOf(existing.size)) airports.set(iata, candidate)
}

const sorted = [...airports.values()].sort((a, b) => a.iata.localeCompare(b.iata))
const countries = Object.fromEntries(
  [...new Set(sorted.map((a) => a.countryCode))].sort().map((code) => {
    let name = code
    try {
      name = countryNames.of(code) ?? code
    } catch {
      // Códigos que la ICU no conoce (p. ej. "XK"): se deja el código, nunca un nombre inventado.
    }
    return [code, name]
  }),
)

const dataset = {
  source: 'OurAirports (https://ourairports.com/data/), dominio público',
  generatedAt: new Date().toISOString().slice(0, 10),
  criteria: 'IATA de 3 letras, large/medium/small_airport, scheduled_service=yes',
  countries,
  airports: sorted.map((a) => [a.iata, a.name, a.city, a.countryCode, a.lat, a.lon, a.size]),
}

writeFileSync(output, JSON.stringify(dataset) + '\n')
console.log(`${sorted.length} aeropuertos, ${Object.keys(countries).length} países → ${output}`)
