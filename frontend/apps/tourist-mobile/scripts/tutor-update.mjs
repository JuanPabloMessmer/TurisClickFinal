#!/usr/bin/env node
// Actualización OTA para que el tutor pruebe la app en su iPhone con Expo Go (ver DISTRIBUTION.md).
//
//   node scripts/tutor-update.mjs check                    exporta el bundle de iOS y lo verifica. No publica.
//   node scripts/tutor-update.mjs publish [-m "mensaje"]   lo mismo y, si la verificación pasa, publica
//                                                          ese mismo bundle en la rama `tutor` de EAS Update.
//   node scripts/tutor-update.mjs verify [carpeta]         sólo verifica un export ya generado.
//
// Por qué existe: `eas update` NO aplica el `env` de eas.json, y Expo carga .env.local con prioridad.
// Sin este script, un .env.local que apunte al backend de la LAN quedaría embebido en el bundle y el
// tutor vería una app que no conecta. Acá se fuerza Azure y se comprueba el resultado antes de publicar.

import { spawnSync } from 'node:child_process'
import { existsSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const EXPORT_DIR = '.tutor-export'
const UPDATE_BRANCH = 'tutor'
const AZURE_API_BASE_URL = 'https://app-turisclick-v2-api.azurewebsites.net'

// URL fija a la propia máquina o a una red privada: localhost, 127.x, 10.x (incluye 10.0.2.2 del
// emulador de Android), 192.168.x y 172.16-31.x.
const PRIVATE_URL_PATTERN =
  /https?:\/\/(?:localhost|127\.\d{1,3}\.\d{1,3}\.\d{1,3}|10\.\d{1,3}\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})(?::\d+)?/g

// URLs de localhost que traen las librerías y que no son el backend de TurisClick: Metro (React Native)
// y el origen por defecto de expo-router/head en desarrollo. Cualquier otra coincidencia hace fallar el check.
//
// Se comparan por prefijo porque el bytecode de Hermes guarda las cadenas pegadas una a otra: lo que
// sigue a la URL puede ser el comienzo de otra cadena (p. ej. "http://localhost:3000" + "1nwz...").
const FRAMEWORK_URL_PREFIXES = ['http://localhost:8081', 'http://localhost:3000']
const isFrameworkUrl = (url) => FRAMEWORK_URL_PREFIXES.some((prefix) => url.startsWith(prefix))

function fail(message) {
  console.error(`\n✖ ${message}`)
  process.exit(1)
}

function buildEnv() {
  const env = { ...process.env, EXPO_NO_DOTENV: '1', EXPO_PUBLIC_API_TARGET: 'azure' }
  // En Windows las variables no distinguen mayúsculas: se borra cualquier variante.
  for (const key of Object.keys(env)) {
    if (key.toUpperCase() === 'EXPO_PUBLIC_API_BASE_URL') delete env[key]
  }
  return env
}

function run(command, env) {
  console.log(`\n$ ${command}`)
  const result = spawnSync(command, { cwd: projectRoot, env, stdio: 'inherit', shell: true })
  if (result.status !== 0) fail(`El comando falló (código ${result.status ?? result.signal}).`)
}

function listFiles(directory) {
  return readdirSync(directory).flatMap((name) => {
    const fullPath = path.join(directory, name)
    return statSync(fullPath).isDirectory() ? listFiles(fullPath) : [fullPath]
  })
}

function verifyExport(exportDir) {
  const bundleDir = path.join(exportDir, '_expo', 'static', 'js', 'ios')
  if (!existsSync(bundleDir)) fail(`No se encontró el bundle de iOS en ${bundleDir}.`)

  const bundles = listFiles(bundleDir).filter((file) => /\.(hbc|js)$/.test(file))
  if (bundles.length === 0) fail(`No hay archivos .hbc/.js en ${bundleDir}.`)

  let hasAzureUrl = false
  const forbidden = new Map()
  const ignored = new Set()

  for (const file of bundles) {
    // latin1 conserva los bytes tal cual: sirve tanto para JS como para bytecode de Hermes, donde las
    // cadenas ASCII (las URLs lo son) quedan legibles.
    const content = readFileSync(file).toString('latin1')
    if (content.includes(AZURE_API_BASE_URL)) hasAzureUrl = true

    for (const [url] of content.matchAll(PRIVATE_URL_PATTERN)) {
      if (isFrameworkUrl(url)) ignored.add(url)
      else forbidden.set(url, path.relative(exportDir, file))
    }
  }

  console.log(`\nBundle verificado: ${bundles.map((file) => path.relative(exportDir, file)).join(', ')}`)
  if (ignored.size > 0) {
    console.log(`  (ignoradas, propias de React Native/Expo: ${[...ignored].join(', ')})`)
  }

  if (!hasAzureUrl) fail(`El bundle no contiene ${AZURE_API_BASE_URL}.`)
  if (forbidden.size > 0) {
    const lines = [...forbidden].map(([url, file]) => `    ${url}  (${file})`).join('\n')
    fail(`El bundle contiene URLs fijas de localhost o de la LAN:\n${lines}\n  No se publica.`)
  }

  console.log(`✔ Contiene ${AZURE_API_BASE_URL}`)
  console.log('✔ Sin URLs fijas de localhost ni de la LAN')
}

function readMessage(args) {
  const index = args.findIndex((arg) => arg === '-m' || arg === '--message')
  const message = index >= 0 ? args[index + 1] : undefined
  const fallback = `Tutor ${new Date().toISOString().slice(0, 16).replace('T', ' ')} UTC`
  // Sin comillas ni caracteres que el shell pueda interpretar.
  return (message ?? fallback).replace(/["'`$%^&|<>\\\r\n]/g, ' ').trim() || fallback
}

const [mode, ...args] = process.argv.slice(2)

if (mode === 'verify') {
  verifyExport(path.resolve(projectRoot, args[0] ?? EXPORT_DIR))
} else if (mode === 'check' || mode === 'publish') {
  const env = buildEnv()
  const exportDir = path.join(projectRoot, EXPORT_DIR)

  rmSync(exportDir, { recursive: true, force: true })
  run(`npx expo export --platform ios --output-dir ${EXPORT_DIR}`, env)
  verifyExport(exportDir)

  if (mode === 'check') {
    console.log('\nModo check: no se publicó nada. Para publicar: npm run tutor:publish')
  } else {
    // --skip-bundler + --input-dir: se publica exactamente el bundle que se acaba de verificar, en vez
    // de dejar que eas-cli vuelva a exportar.
    run(
      `npx eas-cli update --branch ${UPDATE_BRANCH} --platform ios --input-dir ${EXPORT_DIR} --skip-bundler --message "${readMessage(args)}"`,
      env,
    )
    console.log(`\n✔ Publicado en la rama "${UPDATE_BRANCH}".`)
  }
} else {
  fail('Uso: node scripts/tutor-update.mjs <check|publish|verify> [-m "mensaje"]')
}
