import { Tabs } from 'expo-router'
import { Home, Luggage, Search, Sparkles, User } from 'lucide-react-native'
import type { LucideIcon } from 'lucide-react-native'
import type { ColorValue } from 'react-native'
import { colors } from '@/theme/colors'

/**
 * Iconografía vectorial (DESIGN.md §7). Antes la tab bar usaba emoji, con tres problemas: el estado
 * activo no se veía en el ícono —el emoji ignora el `color` que recibe y solo cambiaba la etiqueta—,
 * cada sistema operativo los dibuja distinto, y su alineación óptica obligaba a parchar tamaños.
 */
function TabIcon({ icon: Icon, color, focused }: { icon: LucideIcon; color: ColorValue; focused: boolean }) {
  // expo-router tipa el color del tab como ColorValue; lucide espera el string, que es lo que
  // la navegación entrega en la práctica (uno de los dos tintes declarados abajo).
  return <Icon size={22} color={color as string} strokeWidth={focused ? 2.4 : 1.8} />
}

export default function TabsLayout() {
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: colors.primary,
        tabBarInactiveTintColor: colors.inkMuted,
        tabBarStyle: {
          backgroundColor: colors.surface,
          borderTopColor: colors.border,
          height: 62,
          paddingBottom: 8,
          paddingTop: 6,
        },
        tabBarLabelStyle: { fontSize: 11, fontWeight: '500' },
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'Inicio',
          tabBarIcon: ({ color, focused }) => <TabIcon icon={Home} color={color} focused={focused} />,
        }}
      />
      <Tabs.Screen
        name="explore"
        options={{
          title: 'Explorar',
          tabBarIcon: ({ color, focused }) => <TabIcon icon={Search} color={color} focused={focused} />,
        }}
      />
      <Tabs.Screen
        name="assistant"
        options={{
          title: 'Asistente',
          tabBarIcon: ({ color, focused }) => <TabIcon icon={Sparkles} color={color} focused={focused} />,
        }}
      />
      <Tabs.Screen
        name="trips"
        options={{
          title: 'Mis viajes',
          tabBarIcon: ({ color, focused }) => <TabIcon icon={Luggage} color={color} focused={focused} />,
        }}
      />
      <Tabs.Screen
        name="profile"
        options={{
          title: 'Perfil',
          tabBarIcon: ({ color, focused }) => <TabIcon icon={User} color={color} focused={focused} />,
        }}
      />
    </Tabs>
  )
}
