import { useQueryClient } from '@tanstack/react-query'
import { useOnTouristLeave } from '@/auth/useOnTouristLeave'
import { clearPrivateQueries } from '@/features/reservations/api'

/**
 * Cuando se va un turista —logout, otra cuenta, o un refresh fallido que deja la sesión en nada— se
 * borra de la caché su scope privado. Sin esto, la segunda persona que inicia sesión en el mismo teléfono
 * podría ver por un instante los viajes de la primera.
 *
 * Es la segunda barrera: las keys privadas ya incluyen el id del usuario, así que otra sesión nunca las
 * leería. Esto además libera la memoria y cancela lo que estuviera en vuelo. El catálogo público no se
 * toca, y el paso de invitado a turista no limpia nada (ver useOnTouristLeave): hacerlo cancelaría la
 * primera carga del turista que acaba de entrar.
 */
export function SessionQuerySync() {
  const queryClient = useQueryClient()

  useOnTouristLeave((previousUserId) => {
    void clearPrivateQueries(queryClient, previousUserId)
  })

  return null
}
