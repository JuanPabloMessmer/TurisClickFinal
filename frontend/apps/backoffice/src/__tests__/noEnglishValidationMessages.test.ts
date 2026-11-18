import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

/**
 * Guardia estática de la fase 1 de validaciones (docs/auditoria-validaciones.md, §11.3): en los formularios de la
 * fase 1 ningún `.min(` / `.max(` / `.email(` de zod puede quedar sin mensaje propio (saldría el texto por defecto,
 * en inglés), y las pantallas no vuelven a usar el validador de correo laxo de zod en lugar de la regla compartida.
 */
const SOURCE_ROOT = join(import.meta.dirname, '..')

const PHASE_1_FORMS = [
  'modules/companies/NewProviderAccountPage.tsx',
  'modules/my-company/MyCompanyPage.tsx',
  'modules/administrators/NewAdminAccountPage.tsx',
  'modules/categories/CategoriesPage.tsx',
  'modules/companies/RejectDialog.tsx',
  'modules/companies/FlightSellerCard.tsx',
  'auth/ChangePasswordPage.tsx',
  'auth/LoginPage.tsx',
]

/** `.min(2)`, `.max(150)`, `.email()`: llamadas de zod sin un segundo argumento con el mensaje. */
const WITHOUT_MESSAGE = /\.(min|max|length)\(\s*\d+\s*\)|\.email\(\s*\)/

describe('formularios de la fase 1', () => {
  it.each(PHASE_1_FORMS)('%s no tiene reglas de zod sin mensaje en español', (file) => {
    const source = readFileSync(join(SOURCE_ROOT, file), 'utf8')
    expect(source).not.toMatch(WITHOUT_MESSAGE)
  })
})
