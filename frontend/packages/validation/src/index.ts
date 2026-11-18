/**
 * Reglas de validación compartidas por el backoffice y la app móvil (docs/auditoria-validaciones.md, fase 1).
 * Espejo de `src/TurisClick.Api/Shared/Validation` en el backend; ambos se prueban contra
 * `tests/contract/validation-vectors.json`.
 */
export { LIMITS } from './limits'
export {
  ACCENTED_CHARS,
  UNACCENTED_CHARS,
  countLetters,
  hasExcessiveRepetition,
  hasForbiddenChars,
  multiLine,
  nameKey,
  similarityKey,
  singleLine,
} from './normalize'
export * as rules from './rules'
export type { ParsedPhone, PhoneKind, PlainTextOptions, RuleResult } from './rules'
export {
  categoryNameSchema,
  contactPhoneSchema,
  emailSchema,
  flightSellerRegistrationSchema,
  newPasswordSchema,
  nitSchema,
  organizationNameSchema,
  personNameSchema,
  plainTextSchema,
} from './schemas'
export {
  INVALID_VALUE_MESSAGE,
  NETWORK_MESSAGE,
  SERVER_MESSAGE,
  TIMEOUT_MESSAGE,
  UNEXPECTED_MESSAGE,
  UserMessageError,
  VALIDATION_MESSAGE,
  fieldErrorsOf,
  getErrorMessage,
  isSafeMessage,
  toApiError,
} from './httpErrors'
export type { ApiError } from './httpErrors'
export { applyServerErrors } from './applyServerErrors'
export type { SetFieldError } from './applyServerErrors'
