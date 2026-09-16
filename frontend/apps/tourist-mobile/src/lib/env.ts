import Constants from 'expo-constants'
import { Platform } from 'react-native'

/**
 * Configuración del backend: la ÚNICA fuente de la URL de la API en toda la app.
 *
 * Se elige con variables públicas de Expo (quedan embebidas en el bundle; nunca poner secretos):
 *
 *   EXPO_PUBLIC_API_TARGET=azure   (por defecto) backend desplegado en Azure
 *   EXPO_PUBLIC_API_TARGET=local   backend en la PC de desarrollo, puerto 5288
 *   EXPO_PUBLIC_API_BASE_URL=...   URL puntual; pisa a EXPO_PUBLIC_API_TARGET
 *
 * Con Azure el teléfono no necesita estar en la misma red que la PC. En local, "localhost" significa
 * cosas distintas según dónde corra la app, así que se deduce:
 *
 *   iOS Simulator      → localhost es la Mac         → http://localhost:5288
 *   Android Emulator   → localhost es el emulador    → http://10.0.2.2:5288 (alias al host)
 *   Dispositivo físico → localhost es el teléfono    → http://<IP-LAN-de-la-PC>:5288 (la IP de Metro)
 */

export const AZURE_API_BASE_URL = 'https://app-turisclick-v2-api.azurewebsites.net'
const LOCAL_API_PORT = 5288

export type ApiTarget = 'azure' | 'local' | 'custom'

export interface ApiConfig {
  target: ApiTarget
  baseUrl: string
}

function inferLocalBaseUrl(): string {
  const hostUri = Constants.expoConfig?.hostUri ?? Constants.expoGoConfig?.debuggerHost
  const host = hostUri?.split(':')[0]

  if (Platform.OS === 'android') {
    // El emulador de Android reporta 10.0.2.2/localhost; un teléfono real reporta la IP de LAN.
    const isEmulatorHost = !host || host === 'localhost' || host === '127.0.0.1'
    return `http://${isEmulatorHost ? '10.0.2.2' : host}:${LOCAL_API_PORT}`
  }

  return `http://${host ?? 'localhost'}:${LOCAL_API_PORT}`
}

/** Pura, para poder testearla sin depender del bundle: recibe las variables y cómo deducir la URL local. */
export function resolveApiConfig(
  variables: { target?: string; baseUrl?: string },
  inferLocal: () => string = inferLocalBaseUrl,
): ApiConfig {
  const explicitUrl = variables.baseUrl?.trim()
  if (explicitUrl) return { target: 'custom', baseUrl: explicitUrl.replace(/\/+$/, '') }

  const target = variables.target?.trim().toLowerCase()
  if (target === 'local') return { target: 'local', baseUrl: inferLocal() }

  return { target: 'azure', baseUrl: AZURE_API_BASE_URL }
}

// Acceso literal a process.env.EXPO_PUBLIC_*: es lo que Expo reemplaza al generar el bundle.
export const API_CONFIG = resolveApiConfig({
  target: process.env.EXPO_PUBLIC_API_TARGET,
  baseUrl: process.env.EXPO_PUBLIC_API_BASE_URL,
})

export const API_BASE_URL = API_CONFIG.baseUrl

/**
 * Tiempo máximo por request. El plan gratuito de Azure duerme la app sin uso y el primer request puede
 * tardar mientras arranca; sin un límite, una red caída dejaría la pantalla cargando para siempre.
 */
export const API_TIMEOUT_MS = 60_000

/** Etiqueta corta para Perfil: permite confirmar en el teléfono contra qué backend se está trabajando. */
export const API_TARGET_LABEL: Record<ApiTarget, string> = {
  azure: 'Azure',
  local: 'Local',
  custom: 'Personalizado',
}

/** Versión declarada en app.json; se muestra en Perfil para poder identificar un build al reportar bugs. */
export const APP_VERSION = Constants.expoConfig?.version ?? '0.0.0'

/**
 * true solo en builds de desarrollo (Metro/Expo Go). Habilita herramientas de demostración que no deben
 * existir en producción, como simular un pago rechazado.
 */
export const IS_DEVELOPMENT = typeof __DEV__ !== 'undefined' && __DEV__
