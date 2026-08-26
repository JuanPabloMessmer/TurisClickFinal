import { type ReactNode, useEffect } from 'react'
import { authManager } from './authManager'

/** Dispara la restauración silenciosa de sesión (refresh token guardado) una sola vez al montar la app. */
export function AuthBootstrap({ children }: { children: ReactNode }) {
  useEffect(() => {
    void authManager.bootstrap()
  }, [])

  return children
}
