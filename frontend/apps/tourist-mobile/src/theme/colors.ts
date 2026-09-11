/**
 * Paleta de TurisClick. Vive acá además de en tailwind.config.js porque algunas APIs nativas
 * (StatusBar, ActivityIndicator, gradientes) necesitan el valor y no una clase de utilidad.
 */
export const colors = {
  primary: '#005F73',
  secondary: '#0A9396',
  accent: '#EE9B00',
  background: '#F6F9FA',
  surface: '#FFFFFF',
  ink: '#102A43',
  /** Derivados para texto secundario y bordes — misma familia, sin inventar tonos nuevos. */
  inkMuted: '#5B7285',
  border: '#E2E8F0',
} as const
