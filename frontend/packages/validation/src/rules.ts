import { LIMITS } from './limits'
import { countLetters, hasExcessiveRepetition, hasForbiddenChars, multiLine, singleLine } from './normalize'

/**
 * Reglas de la fase 1 (docs/auditoria-validaciones.md, §7.1). Cada una recibe el valor ya normalizado y
 * devuelve el mensaje en español, o `null` si es válido. Son las mismas que aplica el backend
 * (`Shared/Validation/FieldRules.cs`), con los mismos textos, para que el formulario avise antes de enviar
 * exactamente lo que después diría el servidor.
 */
export type RuleResult = string | null

const ORGANIZATION_CHARS = /^[\p{L}\p{M}0-9 .,&'’\-()/+°–—]$/u
const CATEGORY_CHARS = /^[\p{L}\p{M}0-9 &\-'’,./()]$/u
const PERSON_NAME = /^\p{L}[\p{L}\p{M} '’\-.]*$/u
const REGISTRATION = /^[\p{L}\p{M}0-9 \-./°]+$/u

function firstInvalidChar(value: string, allowed: RegExp): string | null {
  for (let i = 0; i < value.length; i++) if (!allowed.test(value[i])) return value[i]
  return null
}

// ── R-01 · nombre de empresa ─────────────────────────────────────────────────────────────────────────────

export function organizationName(value: string | null, label = 'El nombre de la empresa'): RuleResult {
  if (!value) return `${label} es obligatorio.`
  if (hasForbiddenChars(value)) return `${label} tiene caracteres invisibles o de control.`
  if (value.length < LIMITS.organizationNameMin || value.length > LIMITS.organizationNameMax)
    return `${label} debe tener entre ${LIMITS.organizationNameMin} y ${LIMITS.organizationNameMax} caracteres (tiene ${value.length}).`
  if (countLetters(value) < 2) return `${label} debe incluir al menos dos letras.`
  const invalid = firstInvalidChar(value, ORGANIZATION_CHARS)
  if (invalid) return `${label} tiene un carácter no permitido: «${invalid}».`
  if (hasExcessiveRepetition(value))
    return `${label} repite un mismo carácter más de ${LIMITS.maxRepeatedChars} veces seguidas.`
  return null
}

// ── R-03 · NIT ───────────────────────────────────────────────────────────────────────────────────────────

export const normalizeNit = (value: string | null | undefined): string | null =>
  value == null ? null : value.replace(/[\s.-]/gu, '')

export function nit(normalized: string | null): RuleResult {
  if (!normalized) return 'El NIT es obligatorio.'
  if (!/^[0-9]+$/.test(normalized)) return 'El NIT sólo puede tener números.'
  if (normalized.length < LIMITS.nitMinDigits || normalized.length > LIMITS.nitMaxDigits)
    return `El NIT debe tener entre ${LIMITS.nitMinDigits} y ${LIMITS.nitMaxDigits} dígitos (tiene ${normalized.length}).`
  if (new Set(normalized).size === 1) return 'El NIT no puede ser un mismo dígito repetido.'
  return null
}

// ── R-04 · correo ────────────────────────────────────────────────────────────────────────────────────────

export const normalizeEmail = (value: string | null | undefined): string | null =>
  value == null ? null : value.trim().toLowerCase()

const LOCAL_PART = /^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*$/
const DOMAIN_LABEL = /^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/i
const TOP_LEVEL_DOMAIN = /^[a-z]{2,}$/i

export function email(normalized: string | null, label = 'El correo'): RuleResult {
  if (!normalized) return `${label} es obligatorio.`
  if (/\s/u.test(normalized)) return `${label} no puede tener espacios.`
  if ([...normalized].some((ch) => ch.charCodeAt(0) > 127))
    return `${label} sólo puede tener letras sin tildes, números y los signos . _ - +.`
  if (normalized.length > LIMITS.emailMax)
    return `${label} puede tener hasta ${LIMITS.emailMax} caracteres (tiene ${normalized.length}).`
  const at = normalized.lastIndexOf('@')
  if (at <= 0 || at === normalized.length - 1 || normalized.indexOf('@') !== at)
    return `${label} no tiene un formato válido (falta la @ o hay más de una).`
  const local = normalized.slice(0, at)
  const domain = normalized.slice(at + 1)
  if (local.length > LIMITS.emailLocalPartMax)
    return `La parte antes de la @ puede tener hasta ${LIMITS.emailLocalPartMax} caracteres.`
  if (!LOCAL_PART.test(local)) return `${label} no tiene un formato válido antes de la @.`
  const labels = domain.split('.')
  if (
    labels.length < 2 ||
    labels.some((l) => l.length === 0 || l.length > LIMITS.emailDomainLabelMax || !DOMAIN_LABEL.test(l)) ||
    !TOP_LEVEL_DOMAIN.test(labels[labels.length - 1])
  )
    return `${label} no tiene un dominio válido (por ejemplo, empresa.com.bo).`
  return null
}

// ── R-05 · teléfono de contacto ──────────────────────────────────────────────────────────────────────────

const BOLIVIAN_LANDLINE = /^[234](?!50[1-9]|80\d|90\d|911)[2-689]\d{6}$/
const BOLIVIAN_MOBILE = /^[567]\d{7}$/
const BOLIVIAN_TOLL_FREE = /^800\d{6}$/
const INTERNATIONAL = /^\+[1-9]\d{6,14}$/

export type PhoneKind = 'fijo' | 'celular' | 'línea 800' | 'internacional'

export interface ParsedPhone {
  /** Número en formato internacional (`+591…` para Bolivia), o null si vacío o inválido. */
  value: string | null
  error: RuleResult
  kind: PhoneKind | null
}

/**
 * Normaliza y valida (Plan Técnico Fundamental de Numeración, RM 339/2021): números nacionales de 8 dígitos
 * (fijos 2, 3, 4; celulares 5, 6, 7), líneas 800 de 9 dígitos, con o sin `+591`, y números extranjeros en
 * formato internacional. Vacío es válido: el teléfono es opcional.
 */
export function parsePhone(value: string | null | undefined): ParsedPhone {
  if (value == null || value.trim() === '') return { value: null, error: null, kind: null }
  const compact = value.trim().replace(/[\s.()-]/gu, '')
  const fail = (error: string): ParsedPhone => ({ value: null, error, kind: null })
  if (/\p{L}/u.test(compact)) return fail('El teléfono no puede tener letras.')
  if (/[^0-9+]/.test(compact) || compact.lastIndexOf('+') > 0)
    return fail('El teléfono sólo puede tener números, espacios, guiones y el + inicial.')

  let national: string
  if (compact.startsWith('+591')) national = compact.slice(4)
  else if (compact.startsWith('+'))
    return INTERNATIONAL.test(compact)
      ? { value: compact, error: null, kind: 'internacional' }
      : fail('El número internacional no es válido: escribí el + y el código de país, por ejemplo +54 11 1234 5678.')
  else if (compact.startsWith('00'))
    return fail('Para un número del exterior escribí el + en lugar de 00, por ejemplo +54 11 1234 5678.')
  else if (compact.startsWith('591') && (compact.length === 11 || compact.length === 12)) national = compact.slice(3)
  else if (compact.startsWith('0')) return fail('Escribí el número sin el 0 ni el código de operador.')
  else national = compact

  if (BOLIVIAN_LANDLINE.test(national)) return { value: '+591' + national, error: null, kind: 'fijo' }
  if (BOLIVIAN_MOBILE.test(national)) return { value: '+591' + national, error: null, kind: 'celular' }
  if (BOLIVIAN_TOLL_FREE.test(national)) return { value: '+591' + national, error: null, kind: 'línea 800' }
  if (national.length !== 8 && national.length !== 9)
    return fail('Los números de Bolivia tienen 8 dígitos (o 9 las líneas 800).')
  return fail('No es un número de Bolivia válido: los fijos empiezan con 2, 3 o 4 y los celulares con 5, 6 o 7.')
}

// ── R-06 · nombres de personas ───────────────────────────────────────────────────────────────────────────

export function personName(value: string | null, label: string): RuleResult {
  if (!value) return `${label} es obligatorio.`
  if (hasForbiddenChars(value)) return `${label} tiene caracteres invisibles o de control.`
  if (value.length < LIMITS.personNameMin || value.length > LIMITS.personNameMax)
    return `${label} debe tener entre ${LIMITS.personNameMin} y ${LIMITS.personNameMax} caracteres.`
  if (!PERSON_NAME.test(value))
    return `${label} sólo puede tener letras, espacios, apóstrofes, guiones y puntos, y debe empezar con una letra.`
  return null
}

// ── R-07 · nombre de categoría ───────────────────────────────────────────────────────────────────────────

export function categoryName(value: string | null): RuleResult {
  const label = 'El nombre de la categoría'
  if (!value) return `${label} es obligatorio.`
  if (hasForbiddenChars(value)) return `${label} tiene caracteres invisibles o de control.`
  if (value.length < LIMITS.categoryNameMin || value.length > LIMITS.categoryNameMax)
    return `${label} debe tener entre ${LIMITS.categoryNameMin} y ${LIMITS.categoryNameMax} caracteres (tiene ${value.length}).`
  if (countLetters(value) < 2) return `${label} debe incluir al menos dos letras.`
  const invalid = firstInvalidChar(value, CATEGORY_CHARS)
  if (invalid) return `${label} tiene un carácter no permitido: «${invalid}».`
  if (hasExcessiveRepetition(value))
    return `${label} repite un mismo carácter más de ${LIMITS.maxRepeatedChars} veces seguidas.`
  return null
}

// ── textos libres ────────────────────────────────────────────────────────────────────────────────────────

export interface PlainTextOptions {
  min?: number
  required?: boolean
  multiLine?: boolean
}

export function plainText(raw: string | null | undefined, label: string, max: number, options: PlainTextOptions = {}): RuleResult {
  const value = options.multiLine ? multiLine(raw) : singleLine(raw)
  if (!value) return options.required ? `${label} es obligatorio.` : null
  if (hasForbiddenChars(value, options.multiLine)) return `${label} tiene caracteres invisibles o de control.`
  if (options.min && value.length < options.min) return `${label} debe tener al menos ${options.min} caracteres.`
  if (value.length > max) return `${label} puede tener hasta ${max} caracteres (tiene ${value.length}).`
  return null
}

// ── R-11 · registro o licencia para vender vuelos ────────────────────────────────────────────────────────

export function flightSellerRegistration(raw: string | null | undefined): RuleResult {
  const value = singleLine(raw)
  const label = 'El registro o la licencia'
  if (!value) return `Indicá el registro o la licencia (${LIMITS.flightSellerRegistrationMin} a ${LIMITS.flightSellerRegistrationMax} caracteres).`
  if (value.length < LIMITS.flightSellerRegistrationMin || value.length > LIMITS.flightSellerRegistrationMax)
    return `${label} debe tener entre ${LIMITS.flightSellerRegistrationMin} y ${LIMITS.flightSellerRegistrationMax} caracteres.`
  if (!REGISTRATION.test(value)) return `${label} sólo puede tener letras, números, espacios y los signos - . / °.`
  return null
}
