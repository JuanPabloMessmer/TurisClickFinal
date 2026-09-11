import Constants from 'expo-constants'
import { Platform } from 'react-native'

/**
 * URL del backend. Se toma de EXPO_PUBLIC_API_BASE_URL (.env) y, si no está definida, se deduce un
 * default razonable para desarrollo — nunca una única URL hardcodeada, porque "localhost" significa
 * cosas distintas según dónde corra la app:
 *
 *   iOS Simulator      → localhost es la Mac         → http://localhost:5288
 *   Android Emulator   → localhost es el emulador    → http://10.0.2.2:5288 (alias al host)
 *   Dispositivo físico → localhost es el teléfono    → http://<IP-LAN-de-la-PC>:5288
 *
 * En dispositivo físico se intenta deducir la IP de la máquina que sirve Metro (hostUri), que es
 * justamente la IP de LAN a la que el teléfono ya se conectó.
 */
const DEV_PORT = 5288

function inferDevBaseUrl(): string {
  const hostUri = Constants.expoConfig?.hostUri ?? Constants.expoGoConfig?.debuggerHost
  const host = hostUri?.split(':')[0]

  if (Platform.OS === 'android') {
    // El emulador de Android reporta 10.0.2.2/localhost; un teléfono real reporta la IP de LAN.
    const isEmulatorHost = !host || host === 'localhost' || host === '127.0.0.1'
    return `http://${isEmulatorHost ? '10.0.2.2' : host}:${DEV_PORT}`
  }

  return `http://${host ?? 'localhost'}:${DEV_PORT}`
}

export const API_BASE_URL = process.env.EXPO_PUBLIC_API_BASE_URL ?? inferDevBaseUrl()

/** Versión declarada en app.json; se muestra en Perfil para poder identificar un build al reportar bugs. */
export const APP_VERSION = Constants.expoConfig?.version ?? '0.0.0'
