import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import {
  CancellationStatuses,
  CompanyStatuses,
  PublicationStatuses,
  ReservationStatuses,
} from '@turisclick/api-client'
import { describe, expect, it } from 'vitest'

/**
 * Chequeo estático: un valor de estado tiene que ser válido PARA LA ENTIDAD a la que se le manda.
 *
 * Esto existe por un bug concreto. La tarjeta "Empresas esperando aprobación" de "Hoy" pedía
 * `status: 'PENDING'`, pero el enum de empresas es `PENDING_APPROVAL`: el endpoint respondía 400 y la
 * tarjeta se quedaba girando para siempre. Nadie lo notó porque un 400 en una query de TanStack no rompe
 * nada visible — simplemente no deja de cargar nunca. Y era lo primero que veía un administrador al entrar.
 *
 * La parte importante es que `'PENDING'` **sí existe** en el backend: es un estado válido de pago y de
 * emisión de pasaje. Una lista global de valores permitidos no habría atrapado nada. Lo que hace falta es
 * mirar el contexto: si la llamada habla de empresas, el estado tiene que ser un estado de empresa.
 *
 * TypeScript no cubre esto porque los DTO generados declaran estos campos como `string`.
 */

const SOURCE_ROOT = join(import.meta.dirname, '..')

/** Qué estados acepta cada entidad, según las listas que publica el contrato. */
const POR_ENTIDAD: { pista: RegExp; nombre: string; validos: readonly string[] }[] = [
  { pista: /compan/i, nombre: 'empresa', validos: CompanyStatuses },
  { pista: /reserv/i, nombre: 'reserva', validos: [...ReservationStatuses, ...CancellationStatuses] },
  { pista: /experien/i, nombre: 'experiencia', validos: PublicationStatuses },
  { pista: /packag|paquet/i, nombre: 'paquete', validos: PublicationStatuses },
]

/** Un literal en MAYÚSCULAS asignado a `status`, en código o en una URL. */
const PATRONES = [
  /\bstatus\b\s*[:=]\s*'([A-Z][A-Z_]{3,})'/g,
  /[?&]status=([A-Z][A-Z_]{3,})/g,
]

/** Cuánto texto alrededor se mira para decidir de qué entidad se está hablando. */
const VENTANA = 160

function collectSourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry)
    if (statSync(path).isDirectory()) {
      if (entry === '__tests__' || entry === 'test-setup') return []
      return collectSourceFiles(path)
    }
    return /\.tsx?$/.test(entry) && !/\.test\.tsx?$/.test(entry) ? [path] : []
  })
}

function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1')
}

/** La entidad mencionada más cerca del literal; null si el contexto no la nombra. */
function entidadDe(code: string, index: number) {
  const contexto = code.slice(Math.max(0, index - VENTANA), index + VENTANA)
  return POR_ENTIDAD.find((e) => e.pista.test(contexto)) ?? null
}

describe('los estados que se mandan al backend son válidos para su entidad', () => {
  const files = collectSourceFiles(SOURCE_ROOT)

  it('encuentra archivos para revisar', () => {
    expect(files.length).toBeGreaterThan(30)
  })

  it.each(files.map((file) => [file.slice(SOURCE_ROOT.length + 1), file]))(
    'sin estados que su entidad no acepta en %s',
    (_relative, file) => {
      const code = stripComments(readFileSync(file, 'utf8'))
      const problemas: string[] = []

      for (const regex of PATRONES) {
        regex.lastIndex = 0
        let match: RegExpExecArray | null
        while ((match = regex.exec(code)) !== null) {
          const entidad = entidadDe(code, match.index)
          if (entidad && !entidad.validos.includes(match[1])) {
            problemas.push(`'${match[1]}' no es un estado de ${entidad.nombre} (válidos: ${entidad.validos.join(', ')})`)
          }
        }
      }

      expect(problemas.length === 0 ? null : problemas, problemas.join(' | ')).toBeNull()
    },
  )
})
