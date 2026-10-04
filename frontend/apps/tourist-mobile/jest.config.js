const path = require('path')

/**
 * jest-expo trae el preset de React Native (transform, mocks nativos, entorno).
 *
 * `transformIgnorePatterns` es la parte delicada en un monorepo: por defecto Jest no transpila nada de
 * node_modules, y acá hacen falta dos excepciones — los paquetes de Expo/RN se publican en ESM, y
 * nuestros @turisclick/* se consumen como TypeScript fuente (igual que en Metro, sin build previo).
 *
 * Se replica la lista del preset y se le agregan nativewind y @turisclick. Ojo con el detalle: los
 * prefijos NO llevan separador final a propósito, porque `expo` también tiene que cubrir a
 * `expo-modules-core` y compañía. Jest normaliza estas barras a `\` en Windows por su cuenta.
 */
const TRANSPILED_IN_NODE_MODULES = [
  '.pnpm',
  'react-native',
  '@react-native',
  '@react-native-community',
  'expo',
  '@expo',
  '@expo-google-fonts',
  'react-navigation',
  '@react-navigation',
  '@sentry/react-native',
  'native-base',
  'standard-navigation',
  'nativewind',
  'react-native-css-interop',
  // lucide-react-native y react-native-svg se publican en ESM, igual que los paquetes de Expo.
  'lucide-react-native',
  'react-native-svg',
  '@turisclick',
]

/** Rutas a la copia de esta app, no a la que quede hoisteada en la raíz del monorepo. */
const own = (pkg) => path.resolve(__dirname, 'node_modules', pkg)

module.exports = {
  preset: 'jest-expo',
  setupFilesAfterEnv: [path.resolve(__dirname, 'jest.setup.js')],
  moduleNameMapper: {
    '^@/(.*)$': path.resolve(__dirname, 'src/$1'),
    // El monorepo tiene dos Reacts: el que fija Expo SDK 57 (anidado acá) y el que hoistea el
    // Backoffice. @testing-library/react-native vive hoisteado y resolvería el segundo, con lo que un
    // test correría con dos copias de React a la vez. Se fuerza a todos a la copia de esta app, que es
    // la misma que usa Metro en runtime.
    '^react$': own('react'),
    '^react/(.*)$': path.join(own('react'), '$1'),
    '^react-dom$': own('react-dom'),
    '^react-dom/(.*)$': path.join(own('react-dom'), '$1'),
    // lucide-react-native publica su condición `react-native` como .mjs, que Jest no transforma por
    // extensión. En tests se resuelve su build CommonJS; Metro sigue usando el ESM en runtime.
    '^lucide-react-native$': path.resolve(__dirname, '../../node_modules/lucide-react-native/dist/cjs/lucide-react-native.js'),
  },
  transformIgnorePatterns: [
    `/node_modules/(?!(${TRANSPILED_IN_NODE_MODULES.join('|')}))`,
    '/node_modules/react-native-reanimated/plugin/',
    '/node_modules/@react-native/babel-preset/',
  ],
  collectCoverageFrom: ['src/**/*.{ts,tsx}', 'app/**/*.tsx'],
}
