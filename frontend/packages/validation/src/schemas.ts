import { z } from 'zod'
import { LIMITS } from './limits'
import { singleLine } from './normalize'
import * as rules from './rules'
import type { PlainTextOptions, RuleResult } from './rules'

/**
 * Piezas zod para los formularios (react-hook-form + zodResolver). Validan el valor **normalizado** con las
 * mismas reglas y mensajes que el backend; no transforman lo que el usuario escribió (el servidor normaliza al
 * guardar), así el tipo de entrada y el de salida del formulario son el mismo `string`.
 */
const withRule = (check: (value: string) => RuleResult) =>
  z.string({ error: 'Este campo es obligatorio.' }).superRefine((value, ctx) => {
    const message = check(value)
    if (message) ctx.addIssue({ code: 'custom', message })
  })

export const organizationNameSchema = (label?: string) =>
  withRule((v) => rules.organizationName(singleLine(v), label))

export const nitSchema = () => withRule((v) => rules.nit(rules.normalizeNit(v)))

export const emailSchema = (label?: string) => withRule((v) => rules.email(rules.normalizeEmail(v), label))

/** Teléfono opcional: vacío es válido. */
export const contactPhoneSchema = () => withRule((v) => rules.parsePhone(v).error)

export const personNameSchema = (label: string) => withRule((v) => rules.personName(singleLine(v), label))

export const categoryNameSchema = () => withRule((v) => rules.categoryName(singleLine(v)))

export const plainTextSchema = (label: string, max: number, options?: PlainTextOptions) =>
  withRule((v) => rules.plainText(v, label, max, options))

export const flightSellerRegistrationSchema = () => withRule((v) => rules.flightSellerRegistration(v))

/** Contraseña nueva (D-7): 10 a 200 caracteres. El inicio de sesión no usa este esquema. */
export const newPasswordSchema = () =>
  z
    .string({ error: 'Ingresá la contraseña nueva.' })
    .min(LIMITS.passwordMin, `Usá al menos ${LIMITS.passwordMin} caracteres.`)
    .max(LIMITS.passwordMax, `La contraseña puede tener hasta ${LIMITS.passwordMax} caracteres.`)
