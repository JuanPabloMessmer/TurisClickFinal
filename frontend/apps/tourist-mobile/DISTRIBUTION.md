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

**Actualizar:** volver a correr el mismo comando y reinstalar el APK nuevo encima. El APK publicado se
compiló sin `expo-updates`, así que no recibe OTA: cada cambio implica una build. (`expo-updates` se agregó
después, para Expo Go; antes de la próxima build leé "Riesgos" en la sección de iOS con Expo Go.)

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

Sin cuenta de Apple no hay forma de instalar en iPhone salvo Expo Go: en la misma red, con
`npx expo start` y el QR; a distancia, con EAS Update (sección siguiente).

## iOS — Expo Go + EAS Update (pruebas del tutor, sin costo)

Para que alguien pruebe la app en su iPhone sin Apple Developer Program, sin Metro y sin estar en la misma
red: se publica el bundle de JavaScript en EAS Update y se abre con **Expo Go**. La guía para quien prueba
está en [docs/IOS_TUTOR_TESTING.md](docs/IOS_TUTOR_TESTING.md).

> **Estado:** el export y la verificación del bundle se corrieron en local (`npm run tutor:check`). Todavía
> **no** se publicó ningún update ni se probó en un iPhone real. En Azure están verificadas las lecturas;
> registro, reservas y pagos no.

### Cómo está armado

- `app.json` declara `updates.url` (`https://u.expo.dev/<projectId>`) y **no** declara `runtimeVersion`.
  Es a propósito: Expo Go sólo carga updates dirigidos a la versión del SDK, y un `runtimeVersion` propio
  lo impediría. Por lo mismo **no hay que correr `eas update:configure`**: agrega `runtimeVersion` y
  modifica los perfiles de `eas.json`.
- Los updates van a la rama `tutor` de EAS Update, sólo para iOS.
- Expo Go sólo abre updates de proyectos propios o de una organización de la que la cuenta sea miembro.
  Por eso el proyecto tiene que estar en una organización de Expo y el tutor tiene que ser miembro.

### Procedimiento

1. **Una vez:** transferir el proyecto a la organización en expo.dev, actualizar `owner` en `app.json` con
   el nombre de la organización e invitar al tutor con el rol de menor privilegio que le permita abrirlo.
2. Verificar sin publicar:

   ```bash
   npm run tutor:check
   ```

3. Publicar (requiere `npx eas-cli login`):

   ```bash
   npm run tutor:publish -- -m "Qué cambió"
   ```

4. Abrir en expo.dev la página del update recién publicado (el comando imprime el enlace): de ahí salen
   el QR y el enlace para enviarle al tutor. Probarlo primero en un iPhone propio si hay uno a mano.
5. Para una versión nueva, repetir 2 y 3. El tutor cierra Expo Go y vuelve a abrir el mismo enlace.

`scripts/tutor-update.mjs` hace lo mismo en los dos modos: exporta con `EXPO_NO_DOTENV=1` y
`EXPO_PUBLIC_API_TARGET=azure` (y sin `EXPO_PUBLIC_API_BASE_URL`) a `.tutor-export/`, y comprueba que el
bundle contenga la URL de Azure y ninguna URL fija de `localhost` o de una red privada (`192.168.x`,
`10.x` incluido `10.0.2.2`, `172.16-31.x`, `127.x`). Si la comprobación falla, no publica. En modo
`publish` sube **ese mismo** export (`eas update --input-dir .tutor-export --skip-bundler`), así lo
publicado es exactamente lo verificado.

La comprobación ignora `http://localhost:8081` (Metro) y `http://localhost:3000` (valor por defecto de
`expo-router/head`): vienen dentro de las librerías y no son el backend.

### Riesgos

**1. Bloqueo por la versión del SDK de Expo Go.** La Expo Go de la App Store sólo corre el SDK más
reciente, y en iOS no se puede instalar una versión anterior. Hoy coincide con el proyecto (SDK 58). Cuando
Expo publique el SDK siguiente y el tutor actualice Expo Go (o la instale por primera vez), la app deja de
abrir con un error de SDK incompatible, y no hay arreglo del lado del tutor. Salidas: subir el proyecto al
SDK nuevo y volver a publicar, o pasar a una build propia (TestFlight, con costo). Conviene concentrar las
pruebas del tutor en una ventana corta y revisar el calendario de versiones de Expo antes de fijar fechas.

**2. La próxima compilación nativa: decidir `runtimeVersion`.** `expo-updates` ya es una dependencia, así
que la próxima build de EAS (APK o iOS) lo va a incluir, con `updates.url` y sin `runtimeVersion`. Antes de
compilar hay que decidir:

- Si las builds **deben** recibir OTA: definir `runtimeVersion` (por ejemplo la política `appVersion` o
  `fingerprint`) y un `channel` por perfil en `eas.json`. Atención: a partir de ahí los updates dejan de
  ser cargables por Expo Go, y un update publicado para un runtime que no coincide con el código nativo
  de la build puede hacerla fallar al iniciar.
- Si **no** deben recibir OTA: desactivarlo en esa build (`updates.enabled: false`) o quitar `expo-updates`.

No es compatible servir con la misma configuración a Expo Go y a builds propias; lo más simple es mantener
la configuración actual mientras duren las pruebas con el tutor y resolver esto recién al compilar. No se
verificó cómo se comporta una build de EAS con la configuración actual.

**3. `.env.local`.** En desarrollo apunta al backend de la LAN y Expo lo carga con prioridad sobre `.env`.
Además, el `env` de `eas.json` **no** se aplica a `eas update` (sólo a `eas build`). Un
`npx eas-cli update` corrido a mano publicaría un bundle apuntando a una IP privada, y el tutor vería una
app que no conecta. Por eso los updates del tutor se publican **siempre** con `npm run tutor:publish`,
nunca con `eas update` directo.

**4. Backend en Azure.** Tras un rato sin uso, el primer request tarda cerca de 55 s y el timeout de la app
es de 60 s (`API_TIMEOUT_MS`): el margen es chico y el primer intento puede fallar. Conviene despertar la
API (abrir la app o hacer un request) unos minutos antes de que el tutor empiece.

## Qué probar una vez instalada

Inicio (destinos con foto) → una experiencia → **Elegir fecha** (calendario) → reservar → checkout →
**Mis viajes**; y **Crear cuenta** → onboarding → tab **Asistente** ("Voy 4 días a La Paz…") → ajustar la
propuesta → **¿Por qué?** → guardar → reservar itinerario.
