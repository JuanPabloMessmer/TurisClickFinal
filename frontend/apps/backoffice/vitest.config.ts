import path from 'node:path'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

/**
 * Config propia de tests: el vite.config.ts de la app trae Tailwind, que no aporta nada en jsdom y
 * enlentece cada corrida. Los tests de componentes corren en jsdom; los de lógica pura, igual de bien.
 */
export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@': path.resolve(import.meta.dirname, './src') } },
  test: {
    environment: 'jsdom',
    globals: false,
    restoreMocks: true,
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
    setupFiles: ['src/test-setup/rtl.ts'],
  },
})
