// Metro en un monorepo npm workspaces.
//
// Dos cosas que no vienen por defecto y sin las cuales el bundle falla:
//  1. watchFolders: los packages compartidos viven FUERA de apps/tourist-mobile, así que hay que
//     decirle a Metro que también mire la raíz del monorepo.
//  2. nodeModulesPaths: con hoisting, las dependencias se instalan en frontend/node_modules y no en
//     el node_modules de la app; Metro tiene que buscar en ambos.
//
// No hace falta compilar los packages a JS: @turisclick/* exponen TypeScript directamente
// (main: ./src/index.ts) y Metro los transpila con Babel como a cualquier otro archivo del proyecto,
// porque están dentro de watchFolders.
const { getDefaultConfig } = require('expo/metro-config')
const { withNativeWind } = require('nativewind/metro')
const path = require('node:path')

const projectRoot = __dirname
const monorepoRoot = path.resolve(projectRoot, '../..')

const config = getDefaultConfig(projectRoot)

config.watchFolders = [monorepoRoot]
config.resolver.nodeModulesPaths = [
  path.resolve(projectRoot, 'node_modules'),
  path.resolve(monorepoRoot, 'node_modules'),
]

// NO se desactiva la resolución jerárquica (disableHierarchicalLookup), aunque suela recomendarse en
// monorepos: con hoisting de npm quedan dependencias transitivas anidadas que solo se encuentran
// subiendo el árbol. Caso concreto de este repo: semver@6 queda hoisted en la raíz y no tiene
// `functions/satisfies`, mientras react-native-reanimated trae su propia semver@7 anidada, que es la
// que necesita. Con la búsqueda jerárquica activa Metro encuentra la correcta.

module.exports = withNativeWind(config, { input: './global.css' })
