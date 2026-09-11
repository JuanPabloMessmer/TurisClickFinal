import { useRouter } from 'expo-router'

/**
 * Cerrar una pantalla modal. `router.back()` no hace nada cuando no hay historial —y eso pasa de verdad:
 * al abrir la app desde un deep link a /login, quien inicia sesión se quedaría mirando el formulario.
 * En ese caso se cae a Perfil, que es desde donde se llega a login y registro.
 */
export function useCloseModal() {
  const router = useRouter()
  return () => (router.canGoBack() ? router.back() : router.replace('/profile'))
}
