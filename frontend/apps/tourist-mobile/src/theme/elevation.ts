import { Platform, type ViewStyle } from 'react-native'
import { colors } from '@/theme/colors'

/**
 * Elevación cross-platform (DESIGN.md §6).
 *
 * `elevation` solo existe en Android: declarar únicamente eso —como hacían 28 lugares de esta app—
 * deja todas las tarjetas, hojas y barras perfectamente planas en iPhone, y la jerarquía de
 * profundidad que el diseño asume no llega a la mitad de los dispositivos.
 *
 * Tres niveles y nada más. Una superficie `raised` no contiene otra `raised`.
 */
function shadow(opacity: number, radius: number, offsetY: number, elevation: number): ViewStyle {
  return Platform.select<ViewStyle>({
    android: { elevation },
    default: {
      shadowColor: colors.ink,
      shadowOpacity: opacity,
      shadowRadius: radius,
      shadowOffset: { width: 0, height: offsetY },
    },
  })!
}

export const elevation = {
  /** Sin sombra: la separación la da el borde. */
  flat: {} as ViewStyle,
  /** Tarjetas y barras pegadas al borde de la pantalla. */
  raised: shadow(0.08, 8, 2, 2),
  /** Hojas, diálogos y menús por encima del contenido. */
  overlay: shadow(0.16, 20, 8, 12),
} as const
