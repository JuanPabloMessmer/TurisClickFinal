import { useEffect, useState } from 'react'
import { formatCountdown, secondsLeft } from '@/features/reservations/model'

/** Reloj que avanza cada `intervalMs` mientras esté activo. */
export function useNow(intervalMs = 1000, active = true): number {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    if (!active) return
    setNow(Date.now())
    const timer = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(timer)
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
