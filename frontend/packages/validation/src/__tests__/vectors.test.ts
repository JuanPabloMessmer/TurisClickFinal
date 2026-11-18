import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { LIMITS } from '../limits'
import { nameKey, similarityKey, singleLine } from '../normalize'
import * as rules from '../rules'

/**
 * Mismo archivo de casos que las pruebas de C# (tests/TurisClick.Api.Tests/Shared/Validation): si el frontend y el
 * backend dejan de coincidir en una regla, falla una de las dos suites (docs/auditoria-validaciones.md, §11.1).
 */
interface Case {
  input: string
  valid?: boolean
  normalized?: string | null
  expected?: string
  key?: string
}

const vectors = JSON.parse(
  readFileSync(resolve(import.meta.dirname, '../../../../../tests/contract/validation-vectors.json'), 'utf8'),
) as Record<string, Case[]> & { limits: Record<string, number> }

describe('archivo común de casos', () => {
  it('los límites coinciden con los del backend', () => {
    for (const [name, value] of Object.entries(vectors.limits)) {
      expect(LIMITS[name as keyof typeof LIMITS], name).toBe(value)
    }
  })

  it('texto de una línea', () => {
    for (const c of vectors.singleLine) expect(singleLine(c.input), c.input).toBe(c.expected)
  })

  it('NIT', () => {
    for (const c of vectors.nit) {
      const normalized = rules.normalizeNit(c.input)
      expect(normalized, c.input).toBe(c.normalized)
      expect(rules.nit(normalized) === null, c.input).toBe(c.valid)
    }
  })

  it('teléfono', () => {
    for (const c of vectors.phone) {
      const parsed = rules.parsePhone(c.input)
      expect(parsed.value, c.input).toBe(c.normalized)
      expect(parsed.error === null, `${c.input}: ${parsed.error}`).toBe(c.valid)
    }
  })

  it('correo', () => {
    for (const c of vectors.email) {
      const normalized = rules.normalizeEmail(c.input)
      expect(normalized, c.input).toBe(c.normalized)
      expect(rules.email(normalized) === null, c.input).toBe(c.valid)
    }
  })

  it.each([
    ['organizationName', (v: string) => rules.organizationName(singleLine(v))],
    ['personName', (v: string) => rules.personName(singleLine(v), 'El nombre')],
    ['categoryName', (v: string) => rules.categoryName(singleLine(v))],
  ] as const)('%s', (name, check) => {
    for (const c of vectors[name]) expect(check(c.input) === null, `${name} «${c.input}»: ${check(c.input)}`).toBe(c.valid)
  })

  it('clave de unicidad', () => {
    for (const c of vectors.nameKey) expect(nameKey(c.input), c.input).toBe(c.key)
  })

  it('clave de similitud', () => {
    for (const c of vectors.similarityKey) expect(similarityKey(c.input), c.input).toBe(c.key)
  })

  it('los mensajes son los mismos que devuelve el backend', () => {
    expect(rules.nit('1234')).toBe('El NIT debe tener entre 5 y 12 dígitos (tiene 4).')
    expect(rules.nit('12A45')).toBe('El NIT sólo puede tener números.')
    expect(rules.parsePhone('7ABC4567').error).toBe('El teléfono no puede tener letras.')
    expect(rules.email('a b@x.bo', 'El correo de contacto')).toBe('El correo de contacto no puede tener espacios.')
    expect(rules.organizationName('123')).toBe('El nombre de la empresa debe incluir al menos dos letras.')
    expect(rules.organizationName('Andes <b>')).toBe('El nombre de la empresa tiene un carácter no permitido: «<».')
  })

  it('distingue celular, fijo, 800 e internacional', () => {
    expect(rules.parsePhone('71234567').kind).toBe('celular')
    expect(rules.parsePhone('22123456').kind).toBe('fijo')
    expect(rules.parsePhone('800101234').kind).toBe('línea 800')
    expect(rules.parsePhone('+541112345678').kind).toBe('internacional')
  })
})
