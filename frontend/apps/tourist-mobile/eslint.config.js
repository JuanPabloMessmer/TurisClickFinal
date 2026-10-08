// Lint de Tourist Mobile.
//
// Usa `eslint-config-expo`, que ya trae las reglas de React, de los hooks y de React Native, y entiende el
// router y los módulos nativos de Expo. El Backoffice usa oxlint; no se unifican porque cada uno resuelve
// bien su propio stack.
//
// No hay desactivaciones amplias acá a propósito: si una regla molesta, lo que se arregla es el código.
const { defineConfig } = require('eslint/config')
const expoConfig = require('eslint-config-expo/flat')

module.exports = defineConfig([
  expoConfig,
  {
    // Salidas de build y de export: no son código fuente.
    ignores: ['dist/*', '.expo/*', 'expo-env.d.ts', 'coverage/*'],
  },
  {
    // La configuración de Jest y los tests corren en Node (CommonJS), no en el runtime de la app, así que
    // `module`, `require`, `__dirname` y los globales de Jest existen de verdad ahí. El tsconfig de la app no
    // los declara a propósito, para que el código de producto no pueda usarlos por accidente; declararlos acá
    // es describir el entorno real de estos archivos, no silenciar un problema.
    files: [
      'jest.config.js',
      'jest.setup.js',
      'metro.config.js',
      'babel.config.js',
      '**/__tests__/**/*.{ts,tsx}',
      '**/*.test.{ts,tsx}',
      'src/test-utils/**/*.{ts,tsx}',
    ],
    languageOptions: {
      globals: {
        __dirname: 'readonly',
        __filename: 'readonly',
        require: 'readonly',
        module: 'writable',
        exports: 'writable',
        process: 'readonly',
        jest: 'readonly',
        describe: 'readonly',
        it: 'readonly',
        test: 'readonly',
        expect: 'readonly',
        beforeAll: 'readonly',
        beforeEach: 'readonly',
        afterAll: 'readonly',
        afterEach: 'readonly',
        global: 'writable',
      },
    },
  },
])
