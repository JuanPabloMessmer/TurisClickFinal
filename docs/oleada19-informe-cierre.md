# Oleada 19 — Informe de cierre

**Fecha:** 11/11/2026 · **Commit validado:** `2d75b5d` · **Integrado en:** `master`

Documento de respaldo: [`oleada19-validacion-funcional.md`](oleada19-validacion-funcional.md),
que contiene el detalle prueba por prueba. Este informe consolida el resultado y registra el
estado final del cierre.

> **Estado al 11/11/2026: la Oleada 19 está FUNCIONALMENTE VALIDADA y se da por cerrada para
> fines de desarrollo y documentación, con pendientes administrativos y técnicos explícitamente
> registrados** (§6 y §7). Ninguno de esos pendientes contradice lo validado: son verificaciones
> cuyo criterio exige un entorno al que se decidió no escribir, y deuda técnica preexistente.

---

# 1. QUÉ CAMBIÓ LA OLEADA 19

Los vuelos dejaron de ser un producto que TurisClick vende y pasaron a ser un **metabuscador
con redirección al vendedor**. El cambio tiene dos caras:

| | |
|---|---|
| **Lo que se agregó** | Búsqueda de vuelos que devuelve ofertas de terceros y redirige al sitio del vendedor. En modo DEMO las ofertas se rotulan como no comprables y no redirigen |
| **Lo que se apagó** | La venta directa de vuelos de operador (Oleada 18). Las rutas quedan devolviendo `501` |
| **Lo que debía seguir intacto** | Los paquetes que incluyen vuelo: cotizan, se reservan y se pagan como siempre |

Esa tercera fila es la que concentraba el riesgo: apagar una funcionalidad sin romper la que
comparte código con ella. El commit `2d75b5d` corrige precisamente eso (no cobrar reservas de
vuelo de operador heredadas con la funcionalidad apagada).

---

# 2. DÓNDE SE VALIDÓ, Y QUÉ PUEDE AFIRMAR CADA ENTORNO

La distinción importa porque **los dos entornos no son intercambiables**: uno dice que lo
desplegado es correcto, el otro dice que el comportamiento es correcto. Ninguno solo alcanza.

## 2.1 Azure V2 — sólo lectura

`https://app-turisclick-v2-api.azurewebsites.net`

| | |
|---|---|
| **Pruebas** | 15, todas aprobadas |
| **Naturaleza** | Peticiones de lectura contra la API y el Backoffice desplegados |
| **Qué permite afirmar** | Que **lo desplegado es la Oleada 19** y que el metabuscador responde como debe en producción |
| **Qué NO permite afirmar** | Nada que requiera escribir. No se creó ni modificó un solo registro |
| **Por qué no se escribió** | Decisión explícita del usuario: no tocar datos de producción. Ninguno de los 13 paquetes publicados tenía vuelo configurado, así que el flujo completo era inverificable ahí sin crear datos |

Que el despliegue corresponde a la Oleada 19 se estableció **por comportamiento**, no por número
de migración: la Oleada 19 no agrega migraciones, así que "15 migraciones" no la distingue de la
Oleada 18. Lo que la distingue es la presencia del campo `redirect` en las respuestas de ofertas,
ausente el 27/10 y presente el 09/11.

**Contraste de no-modificación:** 13 paquetes antes, 13 después, 0 de prueba.

## 2.2 Staging local — donde se escribió

`http://localhost:5299` + PostgreSQL local `turisclick_staging_o19`

| | |
|---|---|
| **Pruebas** | 13 automáticas + 5 de aislamiento + 8 manuales |
| **Resultado** | 25 aprobadas · 1 no ejecutada (S-5) · 0 fallidas |
| **Qué permite afirmar** | Que el **comportamiento** es correcto de punta a punta, incluyendo reservar, pagar y cancelar |
| **Por qué era necesario** | Es el único entorno donde se podía ejecutar el flujo completo sin tocar producción |
| **Aislamiento respecto de producción** | Clave JWT propia, base propia, gateway simulado, proveedor de vuelos `Fake`. Demostrado en S-2 |

**S-2 es la prueba que sostiene todo el esquema:** un token emitido por staging devuelve `200`
en staging y `401` en Azure V2. Sin eso, el aislamiento sería una afirmación y no un hecho —
y hacía falta demostrarlo porque `Issuer` y `Audience` son idénticos en todos los ambientes,
de modo que lo único que separa los entornos es la clave de firma.

## 2.3 Las 8 pruebas manuales — origen del resultado

Las ejecutó **el usuario**, en un iPhone físico con Expo Go sobre el SDK 58, contra el staging
local, el 10/11/2026. Reportó las ocho aprobadas.

**No las observé yo, y no hay capturas ni grabaciones.** El registro es el reporte del usuario y
está asentado como tal en §4 del documento de validación. Lo que sí pude verificar, leyendo la
base en sólo lectura, es el rastro que dejan las dos que escriben:

| Dato leído de la base | Qué respalda |
|---|---|
| 2 reservas `CONFIRMED` creadas el 10/11 a las 00:01 y 00:02 | M-07 y M-08 se ejecutaron realmente |
| Sus transacciones de pago: `SUCCEEDED`, proveedor `Simulated` | El pago fue simulado, nunca un gateway real |
| `provider_flights`: **0 filas** | La venta de vuelos de operador sigue apagada |
| `reservation_passengers`: **0 filas** | **No se emitió ningún pasaje** |
| `flight_quotes` / `flight_bookings`: 18 / 3 | La cotización de vuelo del paquete funcionó |

M-01 a M-06 son observaciones de interfaz y no dejan rastro: ahí el único respaldo es el reporte
del usuario.

---

# 3. RESULTADO CONSOLIDADO

| Entorno | Tipo | Pruebas | Aprobadas | Ejecutadas por |
|---|---|---|---|---|
| Azure V2 | Lectura de API y Backoffice | 15 | 15 | Claude |
| Staging local | Funcionales por API | 13 | 13 | Claude |
| Staging local | Aislamiento | 5 | 4 *(1 no ejecutada)* | Claude |
| Staging local | Manuales en Expo | 8 | 8 | **Usuario** |
| | **Total** | **41** | **40** | |

**0 pruebas fallidas. 0 defectos funcionales abiertos.**

La no ejecutada es **S-5**. No se ejecutó deliberadamente: lo que afirma el informe sobre
aislamiento se sostiene con S-2, y S-5 no agregaba garantía sin ampliar el alcance.

## 3.1 Hallazgos abiertos

| # | Hallazgo | Gravedad | Estado |
|---|---|---|---|
| H-02 | `Issuer` y `Audience` iguales en todos los ambientes | Informativa | **Abierto como nota de diseño.** No es un defecto, pero obliga a que cada ambiente tenga clave de firma propia. Conviene dejarlo escrito en la documentación de arquitectura |
| H-03 | El host de Azure V1 no está documentado | Baja | **Abierto.** Impide verificar V1 de forma independiente. Depende de un dato que sólo tenés vos |

H-01 (ningún paquete de Azure tenía vuelo configurado) quedó resuelto por otra vía: la
validación se hizo en staging, sin tocar producción.

---

# 4. ESTADO TÉCNICO DE LA RAMA

## 4.1 `feat/expo-sdk-58` — integrada en `master`

| | |
|---|---|
| **Commits propios** | **5** |
| Integración | fast-forward a `master` el 11/11/2026, sin commit de merge |
| `master` = `origin/master` | `376b9b1` |
| Rama conservada | sí, como trazabilidad |
| Punto de retorno | tag `pre-merge-oleada19` en `2d75b5d` |

El detalle de los cinco commits está en §6.1. El push **no disparó despliegue**: el workflow
`backend-v2.yml` filtra por rutas de `src/**` y `tests/**`, y estos commits tocan sólo
`frontend/` y `docs/`. Verificado: la última corrida del workflow es del 28/10.

El upgrade quedó verificado: **TypeScript 0 errores · ESLint 0 errores** (2 advertencias
preexistentes) · **527–528 de 528 tests**, según la corrida.

> La suite `AssistantChatHistory.test.tsx` es **intermitente** bajo carga de corrida completa: el
> 09/11 pasó 528/528 y el 10/11 falló 1 test de los 8 de esa suite. **Corrida en aislamiento da
> 8/8 de forma consistente.** No es una regresión del upgrade —nada de `frontend/` cambió entre
> ambas corridas—, pero sí significa que aquel verde era una corrida y no un resultado
> determinista. Registrado como deuda técnica en §6.5 y en `TC-220`.

## 4.2 Expo Doctor — 18/20 al 11/11 (19/20 al 10/11)

El único check que falla es **duplicación de React**: la app resuelve 19.3.0 (anidado) y
`react-native`, `expo-router` y NativeWind resuelven 19.3.1 (hoisteado para el Backoffice).

**No lo introdujo el upgrade.** Verifiqué contra el lockfile anterior que la división ya existía
con el SDK 57 (19.2.3 anidado / 19.2.8 hoisteado), y la app venía funcionando así. `jest.config.js`
documenta esta situación y la compensa explícitamente.

**El 11/11 apareció un segundo check en rojo, y no es del proyecto.** Expo publicó parches nuevos
del SDK 58 durante la noche, así que 9 paquetes quedaron **un parche atrás** del conjunto vigente:

| paquete | instalado | vigente al 11/11 |
|---|---|---|
| `expo` | 58.0.23 | ~58.0.24 |
| `expo-constants` | 58.0.15 | ~58.0.16 |
| `expo-font` | 58.0.11 | ~58.0.12 |
| `expo-linear-gradient` | 58.0.3 | ~58.0.4 |
| `expo-linking` | 58.0.16 | ~58.0.17 |
| `expo-router` | 58.0.30 | ~58.0.31 |
| `expo-secure-store` | 58.0.2 | ~58.0.3 |
| `expo-splash-screen` | 58.0.13 | ~58.0.14 |
| `expo-status-bar` | 58.0.4 | ~58.0.5 |

Son **diferencias de parche**, que expo-doctor marca como auto-corregibles, no como incompatibilidad.
Nada del proyecto cambió entre el 10 y el 11 de noviembre: los 53 archivos del upgrade son los
mismos. Lo que se movió fue el upstream.

Se resuelve con `npx expo install --fix`, **pero eso modifica `package.json` y el lockfile**, es
decir el contenido del commit 1. Es una decisión a tomar antes de commitear: actualizar al conjunto
del 11/11 y volver a correr la verificación, o commitear el conjunto del 10/11, que es el que quedó
efectivamente probado con las 8 pruebas manuales en el teléfono.

| | |
|---|---|
| **Causa** | La app móvil fija `react` exacto (lo que pide el SDK) y el Backoffice declara un rango. npm no puede unificar |
| **Arreglo** | Alinear la versión de React del Backoffice con el pin del SDK |
| **Por qué no lo hice** | Toca el Backoffice, que está fuera del alcance de esta oleada. Es una decisión tuya |
| **Riesgo de dejarlo** | El mismo que ya se venía corriendo. No es una regresión |

---

# 5. VERIFICACIÓN PREVIA A DESMANTELAR STAGING

Pedido explícito: comprobar que no se pierda evidencia antes de borrar. Estado al 10/11/2026:

| Elemento | Estado | ¿Se puede borrar? |
|---|---|---|
| API en el puerto 5299 | **Ya detenida** — el proceso terminó al cerrarse la sesión del 09/11 | Nada que hacer |
| Base `turisclick_staging_o19` | **Intacta.** 33 tablas · 15 migraciones · 2 paquetes · 3 reservas · 5 pagos simulados | ⚠️ **Todavía no** |
| `frontend/apps/tourist-mobile/.env.local` | Presente | ✅ Sí, las pruebas terminaron |
| `evidencia-o19.json` y los scripts de la batería | **En el scratchpad de la sesión** — directorio temporal del sistema | ⚠️ **Copiar primero** |

## 5.1 Qué hay que rescatar antes de borrar

Dos cosas, y ninguna es reconstruible después:

1. **Los conteos de la base** (§4.1 del documento de validación). Ya están transcritos en el
   documento, así que esto está cubierto — pero si querés el volcado crudo como anexo de la
   tesis, hay que sacarlo ahora.
2. **`evidencia-o19.json` y los scripts.** Viven en un directorio temporal del sistema, no en el
   repositorio. Si se limpia, se pierden las respuestas JSON de los 13 pasos automáticos.

Sugerencia concreta: una carpeta `docs/evidencia/oleada19/` versionada, con el JSON y los cuatro
scripts (`bateria_o19.py`, `s2_aislamiento.py`, `run_staging.py`, `staging_db.py`). Los scripts
**no contienen secretos** — leen la clave desde user-secrets y no la imprimen —, así que son
versionables tal cual.

Con eso hecho, borrar la base no pierde nada.

---

## 5.2 Checklist de desmantelamiento — preparado, NO ejecutado

Verificado el 10/11/2026: la evidencia ya está preservada en
[`evidencia/oleada19/`](evidencia/oleada19/), redactada y revisada. **Con eso hecho, desmantelar
staging no pierde nada.** El orden importa.

| # | Paso | Comando / acción | Reversible |
|---|---|---|---|
| 1 | Confirmar que la evidencia está commiteada | `git log --oneline -- docs/evidencia/oleada19/` debe devolver un commit | — |
| 2 | Confirmar que la API no está corriendo | `curl http://localhost:5299/api/experiences` debe fallar | — |
| 3 | Borrar la configuración de staging de Expo | borrar `frontend/apps/tourist-mobile/.env.local` | ❌ No, pero es un archivo de 2 líneas que se reescribe |
| 4 | Volcar los conteos finales *(opcional)* | ya transcritos en §4.1 del documento de validación | — |
| 5 | Borrar la base | `DROP DATABASE turisclick_staging_o19` | ❌ **No. Irreversible** |

**El paso 1 es el que no se puede saltear.** Mientras la evidencia siga sólo en el árbol de
trabajo, borrar la base deja la validación sin respaldo recuperable.

No se incluye detener la API porque **ya está detenida**: el proceso terminó al cerrarse la
sesión del 09/11.

### Lo que NO hay que tocar al desmantelar

- `frontend/apps/tourist-mobile/.env` — es la configuración normal de desarrollo
- Las bases `turisclick_v2_dev`, `turisclick_v2_test` y las otras 13 `turisclick_*` locales
- Los user-secrets del proyecto

---

# 6. CIERRE: QUÉ SE HIZO Y QUÉ QUEDÓ PENDIENTE

## 6.1 Versionado — COMPLETADO

Los cinco commits están integrados en `master` por fast-forward y sincronizados con
`origin/master`. El push no disparó despliegue: el workflow `backend-v2.yml` filtra por rutas de
`src/**` y `tests/**`, y los 80 archivos de estos commits son de `frontend/` y `docs/`.

| # | Hash | Asunto | Archivos |
|---|---|---|---|
| 1 | `c9c33ae` | `chore(tourist-mobile): upgrade Expo SDK to 58` | 53 |
| 2 | `5793fcc` | `docs(oleada19): record functional validation and closure evidence` | 9 |
| 3 | `2d2c20d` | `docs(turisclick): align use cases and UML diagrams` | 8 |
| 4 | `0834880` | `docs(scrum): preserve sprint and Jira reconstruction` | 10 |
| 5 | `376b9b1` | `docs(oleada19): clarify Jira references and interview anonymization` | 1 |

El tag `pre-merge-oleada19` marca `2d75b5d`, el punto previo a la integración.

## 6.2 Evidencia — COMPLETADO

Las copias sanitizadas están en [`evidencia/oleada19/`](evidencia/oleada19/), versionadas. Los
originales sin redactar se conservan fuera del repositorio, con herencia de permisos removida.

## 6.3 Staging — CONSERVADO a propósito

No se desmanteló. La base `turisclick_staging_o19`, el archivo `.env.local` y la evidencia
privada siguen en pie, por decisión del autor: el entorno se mantiene mientras pueda hacer falta.
El checklist para desarmarlo cuando corresponda está en §5.2, sin ejecutar.

## 6.4 Pendientes que sólo dependen de datos del autor

Vienen de antes de esta oleada y afectan a la documentación de grado, no al cierre funcional:

| Dato | Dónde impacta |
|---|---|
| Nombre del entrevistado E-3 | `TC-165` (`INV-09`) |
| Resultados de las tres entrevistas | `TC-166` (`INV-10`) |
| Fechas de entrega y defensa de la tesis | 10 ítems `DOC-` sin sprint |
| Validación de las fechas estimadas de abril–mayo | Cronología del informe |
| Host de Azure V1 | Hallazgo H-03 |

## 6.5 Deuda técnica registrada

| Tema | Estado |
|---|---|
| React duplicado en el monorepo | Preexistente al upgrade. Registrado en `TC-220` |
| Nueve paquetes del SDK un parche atrás | Se conservan las versiones con las que se aprobaron las pruebas manuales. Registrado en `TC-220` |
| `AssistantChatHistory.test.tsx` intermitente | 527–528 de 528 en corrida completa; 8/8 en aislamiento. Registrado en `TC-220` |
| `web.output` del SDK 58 | Cambió su valor por omisión a salida de servidor. La app es móvil y no exporta web |
| EPERM intermitente de la caché de Jest en Windows | Ambiental. Se resuelve con `npx jest --clearCache` |

---

# 7. ESTADO FINAL EN JIRA — ejecutado el 11/11/2026

Catorce ítems etiquetados `oleada-19` o `deuda-tecnica`: **8 en `Done`, 6 abiertos**. Las claves
`TC-xxx` salen de [`jira-import/07-sprints-y-claves.md`](jira-import/07-sprints-y-claves.md).

## 7.1 Cerrados — 8

| Clave | Ítem | Qué lo respalda |
|---|---|---|
| `TC-90` | `[UC-T-25]` Buscar vuelos entre dos aeropuertos | Implementación, cerrado antes de esta oleada |
| `TC-91` | `[UC-T-26]` Abrir la oferta en el sitio del vendedor | Ídem |
| `TC-111` | `[BUG-10]` Se cobraban reservas de vuelo heredadas | Ídem |
| `TC-181` | `[PEND-01]` Desplegar el backend V2 en Azure | Ambos criterios. Despliegue confirmado por comportamiento (§2.1) y run `workflow_dispatch` del 28/10 02:53 con `headSha 2d75b5d6` |
| `TC-182` | `[PEND-02]` Verificar que `redirect` aparece | A-01 a A-05 contra Azure, sin sesión |
| `TC-184` | `[PEND-04]` Backoffice sin la navegación de Vuelos | A-14 y A-15, contra el Backoffice desplegado |
| `TC-218` | Actualizar Tourist Mobile a Expo SDK 58 | Commit `c9c33ae` en `master`; TypeScript y ESLint en cero; M-01 a M-08 aprobadas |
| `TC-219` | Registrar la validación funcional de la Oleada 19 | Documentación y evidencia versionadas en `master` |

Cada uno lleva en Jira un comentario con la evidencia y el alcance de la verificación.

## 7.2 ⚠️ Abiertos porque su criterio exige Azure y se validó en staging — 3

**Corrección sobre versiones anteriores de este informe:** `TC-187` figuraba aquí como cerrable.
Era un error. Al leer su criterio de aceptación textual —*"Un paquete con vuelo se reserva y se
paga normalmente **en Azure**, con los vuelos de operador apagados"*— queda claro que **no se
cumple**: el flujo se validó en staging local. Es el mismo caso que `TC-183` y `TC-186`.

| Clave | Ítem | Qué se verificó, y dónde |
|---|---|---|
| `TC-183` | `[PEND-03]` Vuelos de operador responden **501 en Azure** | T-12 comprobó el 501 en las dos rutas con sesión válida, **en staging**. En Azure nunca se hizo una petición autenticada a esas rutas |
| `TC-186` | `[PEND-06]` **En Azure**, una reserva heredada no se cobra | T-06, la regresión de BUG-10, lo comprobó de punta a punta **en staging**. En Azure no se escribió |
| `TC-187` | `[PEND-07]` Paquetes con vuelo se siguen pagando **en Azure** | T-06 por API y M-07/M-08 a mano, ambas **en staging**. Además, ninguno de los 13 paquetes publicados en Azure tiene reglas de vuelo configuradas |

Los tres comparten la misma causa: **el criterio se escribió asumiendo que se podía operar y
escribir contra producción**, y esa premisa cambió cuando se decidió no tocar datos reales. El
comportamiento está demostrado; lo que falta es el entorno. Cerrarlos exigiría reescribir el
criterio, que es una decisión del autor y no se tomó.

## 7.3 Abiertos por verificación incompleta o ausente — 2

| Clave | Ítem | Situación |
|---|---|---|
| `TC-193` | `[PEND-13]` Verificar el metabuscador DEMO de punta a punta | De sus **dos** criterios, el primero está cumplido (A-04, A-05 en Azure y M-02, M-03, M-05 a mano). El segundo —*"desde la ruta se llega a los paquetes publicados que la cubren"*— **no lo cubrió ninguna prueba**: M-06 sólo verificó que metabuscador y paquetes son secciones separadas, que no es lo mismo |
| `TC-189` | `[PEND-09]` Configurar `Flights:RedirectHosts` | **Sin verificar.** Es configuración de Azure, que no se tocó |

## 7.4 Deuda técnica — 1

| Clave | Ítem | Estado |
|---|---|---|
| `TC-220` | React duplicado y Expo Doctor 18/20 en Tourist Mobile | Abierto a propósito. Recoge los tres puntos de §6.5 sin corregir ninguno |

## 7.5 Lo que NO se hizo en Jira

No se modificaron criterios de aceptación, ni otros ítems fuera de los nombrados, ni épicas, ni
sprints. A los tres ítems creados no se les asignó sprint: el trabajo cae en el borde entre el
Sprint 16 y el 17, y asignarlo sin evidencia falsearía la cronología.

## 7.6 Seudonimización de los entrevistados

El 11/11/2026, antes de versionar `docs/jira-import/`, los tres entrevistados pasaron a figurar
por código —**E-1**, **E-2** y **E-3**— con una descripción genérica del tipo de informante, en
lugar de su nombre y organización. Son 7 reemplazos en `00-turisclick-completo.csv`,
`03-tareas.csv` y `08-informe-final.md`.

La correspondencia se conserva **fuera del repositorio**, en la copia privada de la evidencia.
El seudónimo es provisional y reversible: si para la defensa corresponde nombrarlos, con su
consentimiento, la tabla privada permite restituirlos. **Jira no se modificó**: allá los ítems
siguen con los nombres originales.

---
# 8. LO QUE NO SE HIZO

Restricciones respetadas, para que quede constancia:

- **Azure V1 y V2 no se modificaron.** Contra V1 no se ejecutó ni una petición. Contra V2, sólo lecturas
- **No se conectó a bases de producción** para migrar ni escribir
- **No se ejecutó merge, push ni deploy**
- **No se modificó Jira**
- **No se borró la base de staging**
- **No se habilitaron Duffel ni pasarelas de pago reales**
- **No se sobrescribieron los `.env` existentes** — staging usó un `.env.local` separado
- **No se expusieron secretos** en ningún documento ni salida de consola

---

# 9. CONCLUSIÓN

> **La Oleada 19 queda cerrada para fines de desarrollo y documentación**, funcionalmente
> validada, con sus pendientes explícitamente registrados. 40 de 41 verificaciones aprobadas,
> ninguna fallida, ningún defecto funcional abierto.

Las dos afirmaciones que importaban quedaron demostradas con evidencia, cada una en el entorno
que correspondía:

- **En Azure V2**, que lo desplegado es la Oleada 19 y que el metabuscador funciona en
  producción. Quince verificaciones de sólo lectura; no se escribió un solo registro.
- **En staging local**, que apagar la venta de vuelos de operador no rompió los paquetes con
  vuelo: reserva, pago simulado y cero emisión de pasajes, verificado en base. Veinticinco
  verificaciones aprobadas, incluidas las ocho manuales reportadas por el autor.

El trabajo está versionado en `master` (§6.1), la evidencia preservada y sanitizada (§6.2), y
Jira refleja el estado real: ocho ítems cerrados y seis abiertos (§7).

## Qué queda abierto, y por qué no impide cerrar

| Pendiente | Naturaleza |
|---|---|
| `TC-183`, `TC-186`, `TC-187` | **Administrativo, no funcional.** El comportamiento está demostrado; el criterio exige un entorno al que se decidió no escribir |
| `TC-193` | Un criterio de dos sin verificar |
| `TC-189` | Configuración de Azure, no verificada |
| `TC-220` | Deuda técnica, dos de cuyos tres puntos son preexistentes al upgrade |
| Datos del autor (§6.4) | Afectan a la documentación de grado, no al software |

Ninguno de estos pendientes contradice lo validado. Los tres primeros son el mismo caso: un
criterio escrito cuando se daba por hecho que se podía operar contra producción, premisa que
cambió. Quedan registrados tal como están, sin reescribir el criterio ni dar por cumplido lo que
no se verificó.

Con esto, la siguiente prioridad del proyecto pasa a ser el documento académico.
