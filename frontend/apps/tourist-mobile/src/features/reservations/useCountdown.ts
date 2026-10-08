import { useEffect, useState } from 'react'
import { formatCountdown, secondsLeft } from '@/features/reservations/model'

/** Reloj que avanza cada `intervalMs` mientras esté activo. */
export function useNow(intervalMs = 1000, active = true): number {
  const [now, setNow] = useState(() => Date.now())

  // Al (re)activarse hay que poner el reloj en hora: si estuvo pausado, `now` quedó viejo y la cuenta
  // regresiva mostraría un valor desactualizado hasta el primer tick. Esa puesta en hora va en el propio
  // temporizador, con un tick inmediato, y no en el cuerpo del efecto: leer el reloj durante el render es
  // impuro, y un setState sincrónico dentro del efecto encadena un render de más en cada activación.
  useEffect(() => {
    if (!active) return

    const sync = () => setNow(Date.now())
    const immediate = setTimeout(sync, 0)
    const timer = setInterval(sync, intervalMs)

    return () => {
      clearTimeout(immediate)
      clearInterval(timer)
    }
  }, [intervalMs, active])

  return now
}

/**
 * Cuenta regresiva hasta `expiresAt` con el reloj del dispositivo. Es solo presentación: que llegue a
 * 00:00 no cambia el estado de la reserva — eso lo decide el backend.
 */
export function useCountdown(expiresAt: string | null | undefined) {
  const active = Boolean(expiresAt)
  const now = useNow(1000, active)

  if (!expiresAt) return { now, secondsLeft: null, label: null, isTimeUp: false }

  const seconds = secondsLeft(expiresAt, now)
  return { now, secondsLeft: seconds, label: formatCountdown(seconds), isTimeUp: seconds === 0 }
}
