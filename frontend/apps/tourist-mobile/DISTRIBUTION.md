# Distribuir Tourist Mobile

La app instalada habla siempre con el backend V2 de Azure
(`https://app-turisclick-v2-api.azurewebsites.net`): no necesita Metro, ni la PC, ni estar en la misma red.
Eso lo fija `eas.json` (`EXPO_PUBLIC_API_TARGET=azure` en los tres perfiles) sobre el default de
`src/lib/env.ts`.

| Dato | Valor |
|---|---|
| Proyecto EAS | `@jpmessmer/turisclick-tourist` |
| Package Android / bundle iOS | `com.turisclick.tourist` |
| Versión | `1.0.0` (los números de build los maneja EAS: `appVersionSource: remote`) |
| Perfiles | `development` (dev client), `preview` (APK interno / IPA ad hoc), `production` (AAB / App Store) |

## Android — APK de distribución interna

```bash
cd frontend/apps/tourist-mobile
npx eas-cli build --platform android --profile preview
```

EAS compila en la nube y devuelve un link de descarga (también queda en
<https://expo.dev/accounts/jpmessmer/projects/turisclick-tourist/builds>). La firma la maneja EAS con un
keystore generado en la nube: no hay credenciales en el repo ni en la PC.

**Build actual (27/09/2026)** — perfil `preview`, versión 1.0.0 (versionCode 1), 95 MB:
<https://expo.dev/artifacts/eas/Anon6ySQ87rMlseQK4H6sy4chX0KHybjgAJPat3p7Pw.apk>
(también en <https://expo.dev/accounts/jpmessmer/projects/turisclick-tourist/builds/800291e5-d86b-4462-862c-f32b057c8093>)

**Instalar en un teléfono Android:**

1. Abrí el link de la build (o el QR que muestra EAS) desde el teléfono.
2. Tocá **Install** y aceptá "Instalar apps desconocidas" para el navegador cuando Android lo pida.
3. Abrí **TurisClick**. Si es la primera vez en un rato, el backend F1 puede tardar unos segundos en
   despertarse.

**Actualizar:** volver a correr el mismo comando y reinstalar el APK nuevo encima. No hay OTA (no está
instalado `expo-updates`), así que cada cambio implica una build.

## iOS — TestFlight

El proyecto ya está preparado: `bundleIdentifier`, `ITSAppUsesNonExemptEncryption` declarado (así
TestFlight no pregunta por compliance en cada build) y el perfil `preview`/`production` en `eas.json`.

Falta lo único que EAS no puede hacer sin vos: **iniciar sesión con tu Apple ID** (y su 2FA) para crear el
certificado de distribución y el provisioning profile. Requiere una cuenta del **Apple Developer Program**
(99 USD/año).

```bash
cd frontend/apps/tourist-mobile
npx eas-cli build --platform ios --profile production   # pide Apple ID + 2FA la primera vez
npx eas-cli submit --platform ios --latest              # sube el build a App Store Connect
```

Después, en App Store Connect: **TestFlight → Internal Testing**, agregás tu correo como tester y te llega
la invitación. En el teléfono: instalar **TestFlight** desde la App Store y aceptar la invitación.

Sin cuenta de Apple no hay forma de instalar en iPhone salvo Expo Go, que sí funciona hoy:
`npx expo start` y escanear el QR.

## Qué probar una vez instalada

Inicio (destinos con foto) → una experiencia → **Elegir fecha** (calendario) → reservar → checkout →
**Mis viajes**; y **Crear cuenta** → onboarding → tab **Asistente** ("Voy 4 días a La Paz…") → ajustar la
propuesta → **¿Por qué?** → guardar → reservar itinerario.
