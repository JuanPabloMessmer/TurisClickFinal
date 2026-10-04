import { LinearGradient } from 'expo-linear-gradient'
import { View } from 'react-native'

/**
 * Velo sobre fotografía (DESIGN.md §8). Degradado vertical de transparente a brand-900: el texto queda
 * legible sobre cualquier foto sin apagar la imagen entera, y medido da 9.3:1 en blanco incluso con una
 * foto clara debajo.
 *
 * Es un componente y no una clase suelta para que el degradado sea el mismo en todas las pantallas:
 * un velo distinto por pantalla es lo que hace que una app se vea armada por partes.
 */
export function Scrim({ height = '70%' }: { height?: number | `${number}%` }) {
  return (
    <View className="absolute inset-x-0 bottom-0" style={{ height }} pointerEvents="none">
      <LinearGradient
        colors={['rgba(6,48,60,0)', 'rgba(6,48,60,0.55)', 'rgba(6,48,60,0.92)']}
        locations={[0, 0.55, 1]}
        style={{ flex: 1 }}
      />
    </View>
  )
}
