import { useEffect, useRef } from 'react'
import { useSession } from '@/auth/session'

/**
 * Avisa cuando SE VA un turista conocido: logout, refresh fallido que deja la sesión vacía, o cambio
 * directo a otra cuenta. Recibe el id de quien se fue.
 *
 * No dispara al pasar de invitado a turista: ahí no hay nada de otra persona que limpiar, y quien estaba
 * navegando sin cuenta conserva su contexto al iniciar sesión. Tampoco decide nada mientras la sesión se
 * restaura o se inicia (status idle/loading), porque el usuario todavía es indeterminado.
 *
 * Existe porque las pantallas de las tabs quedan montadas entre sesiones: su estado local (y la caché)
 * sobrevive a un logout si nadie lo resetea.
 */
export function useOnTouristLeave(onLeave: (previousUserId: string) => void) {
  const { status, isAuthenticated, user } = useSession()
  const previousUserId = useRef<string | null | undefined>(undefined)
  const callback = useRef(onLeave)
  callback.current = onLeave

  useEffect(() => {
    if (status === 'idle' || status === 'loading') return

    const currentUserId = isAuthenticated ? (user?.id ?? null) : null
    const previous = previousUserId.current
    previousUserId.current = currentUserId

    if (typeof previous === 'string' && previous !== currentUserId) {
      callback.current(previous)
    }
  }, [status, isAuthenticated, user?.id])
}
