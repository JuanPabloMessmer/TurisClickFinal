import { LIMITS } from './limits'

/**
 * Normalización de textos antes de validarlos (docs/auditoria-validaciones.md, §6). Es la misma que aplica el
 * backend (`Shared/Validation/TextNormalizer.cs`) antes de validar y guardar, así el formulario evalúa lo mismo
 * que va a evaluar el servidor.
 */

/** Espacios según .NET `char.IsWhiteSpace`: separadores Unicode (Zs, Zl, Zp) y los de control clásicos. */
const WHITESPACE = /[\p{Zs}\p{Zl}\p{Zp}\t\n\v\f\r\u0085]/u
const FORBIDDEN_CATEGORY = /[\p{Cc}\p{Cf}\p{Co}\p{Cn}]/u

/** Vocales acentuadas y su versión sin tilde. La ñ no está: es una letra distinta (Peña ≠ Pena). */
export const ACCENTED_CHARS = 'ÁÀÂÄÃÉÈÊËÍÌÎÏÓÒÔÖÕÚÙÛÜÑáàâäãéèêëíìîïóòôöõúùûü'
export const UNACCENTED_CHARS = 'aaaaaeeeeiiiiooooouuuuñaaaaaeeeeiiiiooooouuuu'

const LEGAL_FORMS = new Set(['srl', 'sa', 'ltda'])

/** Texto de una línea: NFC, cualquier espacio pasa a ser uno común, sin repetidos ni en los extremos. */
export function singleLine(value: string): string
export function singleLine(value: string | null | undefined): string | null
export function singleLine(value: string | null | undefined): string | null {
  if (value == null) return null
  let out = ''
  let pendingSpace = false
  for (const unit of value.normalize('NFC').split('')) {
    if (WHITESPACE.test(unit)) {
      pendingSpace = out.length > 0
      continue
    }
    if (pendingSpace) out += ' '
    pendingSpace = false
    out += unit
  }
  return out
}

/** Texto de varias líneas: NFC, saltos de línea unificados y extremos recortados. */
export function multiLine(value: string | null | undefined): string | null {
  return value == null ? null : value.normalize('NFC').replace(/\r\n?/g, '\n').trim()
}

/**
 * true si el texto tiene caracteres de control (salvo el salto de línea cuando se permite), invisibles de
 * formato (ancho cero, BOM, marcas de dirección), de uso privado, sin asignar o sustitutos UTF-16 (como en .NET,
 * un emoji cuenta como sustituto y no se acepta en nombres).
 */
export function hasForbiddenChars(value: string, allowNewLines = false): boolean {
  for (let i = 0; i < value.length; i++) {
    const code = value.charCodeAt(i)
    if (allowNewLines && code === 10) continue
    if (code >= 0xd800 && code <= 0xdfff) return true
    if (FORBIDDEN_CATEGORY.test(value[i])) return true
  }
  return false
}

/** Clave de unicidad (K1): una línea, minúsculas y sin tildes ni diéresis. Igual a la de la base. */
export function nameKey(value: string): string {
  let out = ''
  for (const ch of singleLine(value).toLowerCase()) {
    const index = ACCENTED_CHARS.indexOf(ch)
    out += index >= 0 ? UNACCENTED_CHARS[index] : ch
  }
  return out
}

/** Clave de similitud (K2): K1 sin puntuación ni forma societaria final. Sólo para advertir. */
export function similarityKey(value: string): string {
  let out = ''
  for (const ch of nameKey(value)) {
    if (/[\p{L}\p{Nd}]/u.test(ch)) out += ch
    else if (ch === ' ') out += ' '
  }
  const words = out.split(' ').filter((w) => w.length > 0)
  while (words.length > 1 && LEGAL_FORMS.has(words[words.length - 1])) words.pop()
  return words.join(' ')
}

/** true si algún carácter que no es dígito se repite más de `max` veces seguidas. */
export function hasExcessiveRepetition(value: string, max: number = LIMITS.maxRepeatedChars): boolean {
  let run = 1
  for (let i = 1; i < value.length; i++) {
    if (value[i] === value[i - 1] && !/\p{Nd}/u.test(value[i])) {
      run++
      if (run > max) return true
    } else run = 1
  }
  return false
}

export const countLetters = (value: string): number => (value.match(/\p{L}/gu) ?? []).length
