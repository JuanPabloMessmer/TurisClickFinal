/**
 * Límites de las reglas de la fase 1 (docs/auditoria-validaciones.md, §7.1). Los mismos valores viven en
 * `src/TurisClick.Api/Shared/Validation/ValidationLimits.cs`; el archivo común `tests/contract/validation-vectors.json`
 * verifica en las dos suites que no diverjan.
 */
export const LIMITS = {
  organizationNameMin: 3,
  organizationNameMax: 100,
  companyDescriptionMax: 1000,
  nitMinDigits: 5,
  nitMaxDigits: 12,
  emailMax: 254,
  emailLocalPartMax: 64,
  emailDomainLabelMax: 63,
  personNameMin: 2,
  personNameMax: 100,
  categoryNameMin: 3,
  categoryNameMax: 40,
  categoryDescriptionMax: 300,
  rejectionReasonMin: 5,
  rejectionReasonMax: 500,
  flightSellerRegistrationMin: 3,
  flightSellerRegistrationMax: 100,
  passwordMin: 10,
  passwordMax: 200,
  maxRepeatedChars: 3,
} as const
