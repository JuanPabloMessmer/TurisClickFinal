import { Stack } from 'expo-router'

/**
 * Login y registro viven fuera de las tabs y se presentan como modal desde la raíz: quien estaba viendo
 * una experiencia vuelve exactamente ahí al cerrar, sin perder el scroll ni la navegación.
 */
export default function AuthLayout() {
  return <Stack screenOptions={{ headerShown: false }} />
}
