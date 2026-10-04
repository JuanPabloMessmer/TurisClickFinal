/**
 * Tokens de color de TurisClick — Tourist Mobile. La autoridad es DESIGN.md en la raíz del repo.
 *
 * Viven acá además de en tailwind.config.js porque algunas APIs nativas (StatusBar, ActivityIndicator,
 * sombras, gradientes) necesitan el valor y no una clase de utilidad. Los dos archivos tienen que
 * declarar lo mismo: si un token existe acá y no en la config de Tailwind, su utilidad no existe y
 * termina reescrito como literal en cada pantalla — que es exactamente lo que pasó con `inkMuted`.
 */
export const colors = {
  /** Marca compartida con el Backoffice. */
  brand900: '#06303C',
  brand700: '#005F73',
  brand500: '#0E7A8E',
  /** Alias: `primary` es el que usa la app para acciones y estados activos. */
  primary: '#005F73',
  secondary: '#0E7A8E',

  /** Acento: fill solo con texto `ink` (5.44:1). Como texto o indicador sobre claro va accent700 (5.08:1). */
  accent: '#E08A00',
  accent700: '#A85A08',

  background: '#F6F9FA',
  surface: '#FFFFFF',
  ink: '#102A43',
  inkMuted: '#5B7285',

  /** `border` separa (1.2:1, legal para un divisor); `borderControl` delimita algo que se toca (3:1). */
  border: '#E2E8F0',
  borderControl: '#828E9C',

  /** Estados: texto 800 sobre fondo 100 — 6.4–7.2:1. */
  successFg: '#166534',
  successBg: '#DCFCE7',
  warningFg: '#92400E',
  warningBg: '#FEF3C7',
  dangerFg: '#991B1B',
  dangerBg: '#FEE2E2',
  infoFg: '#1E40AF',
  infoBg: '#DBEAFE',
} as const
