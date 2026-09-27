import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// Con `globals: false` Testing Library no limpia sola entre tests: sin esto, cada render se apila en el
// mismo documento y las queries encuentran elementos de tests anteriores.
afterEach(cleanup)
