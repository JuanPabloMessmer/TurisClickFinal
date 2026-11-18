import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { z } from 'zod'
import App from './App'
import './index.css'

// Red de seguridad: si algún esquema zod quedara sin mensaje propio, su error sale en español y no con el texto
// por defecto en inglés (docs/auditoria-validaciones.md, §9.2).
z.config(z.locales.es())

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
