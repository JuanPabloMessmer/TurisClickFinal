# Oleada 19 — Informe de cierre

**Fecha:** 10/11/2026 · **Commit validado:** `2d75b5d` · **Rama de trabajo:** `feat/expo-sdk-58`

Documento de respaldo: [`oleada19-validacion-funcional.md`](oleada19-validacion-funcional.md),
que contiene el detalle prueba por prueba. Este informe consolida el resultado y dice qué falta
para dar la oleada por cerrada formalmente.

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

## 4.1 `feat/expo-sdk-58`

| | |
|---|---|
| **Commits propios** | **0** |
| `HEAD` | `2d75b5d` — el mismo que `master` |
| **Cambios sin versionar** | **65 entradas** |
| └ del upgrade a SDK 58 | 53 archivos en `frontend/` |
| └ de documentación | 12 entradas en `docs/` |

> ⚠️ **Esto es el riesgo más alto del proyecto ahora mismo.** Todo el upgrade al SDK 58 —
> incluida la migración de 46 archivos de test — existe **únicamente como cambios sin commitear
> en el árbol de trabajo**. Un `git checkout`, un `git stash` mal usado o un reset lo borra sin
> recuperación. La rama está creada pero vacía: no protege nada todavía.

El upgrade quedó verificado: **TypeScript 0 errores · ESLint 0 errores** (2 advertencias
preexistentes) · **527–528 de 528 tests**, según la corrida.

> La suite `AssistantChatHistory.test.tsx` es **intermitente** bajo carga de corrida completa: el
> 09/11 pasó 528/528 y el 10/11 falló 1 test de los 8 de esa suite. **Corrida en aislamiento da
> 8/8 de forma consistente.** No es una regresión del upgrade —nada de `frontend/` cambió entre
> ambas corridas—, pero sí significa que el verde de ayer era una corrida y no un resultado
> determinista. Registrado como deuda técnica en §6.6.

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

# 6. QUÉ FALTA PARA CERRAR FORMALMENTE

Funcionalmente la oleada está validada. Lo que resta es de versionado y registro.

## 6.1 Bloqueante — antes de cualquier otra cosa

| # | Acción | Por qué |
|---|---|---|
| 1 | **Commitear el upgrade al SDK 58** en `feat/expo-sdk-58` | 53 archivos verificados existen sólo en el árbol de trabajo. Es lo único verdaderamente urgente |
| 2 | **Commitear los documentos de `docs/`** | 12 entradas, incluidos este informe, la validación, el plan de staging y el material de Jira |

Conviene separarlo en dos commits: el upgrade es un cambio técnico y los documentos son registro
del proyecto. Mezclarlos ensucia la historia que después citás en la tesis.

## 6.2 Antes de desarmar staging

| # | Acción |
|---|---|
| 3 | Copiar `evidencia-o19.json` y los scripts a `docs/evidencia/oleada19/` |
| 4 | Borrar `frontend/apps/tourist-mobile/.env.local` |
| 5 | Recién entonces `DROP DATABASE turisclick_staging_o19` |

## 6.3 Cierre del registro

| # | Acción | Dependencia |
|---|---|---|
| 6 | Mover a *Done* en Jira los ítems de la Oleada 19 | Requiere tu autorización: no toqué Jira |
| 7 | Decidir si el upgrade al SDK 58 se registra como ítem de Sprint | Es trabajo real no planificado, surgido de que Expo Go dejó de soportar el SDK 57 |
| 8 | Decidir el destino de la rama: merge a `master` o quedarse en la rama | No hice merge ni push |

## 6.4 Plan de commits — archivos exactos, pendiente de aprobación

Revisado archivo por archivo. **Ningún secreto**: `.env`, `.env.local`, `node_modules/`, `dist/`
y `.expo/` están en `.gitignore` y no pueden entrar. La evidencia se incorporó redactada (§5.1).

### Commit 1 — `chore(tourist-mobile): upgrade Expo SDK to 58`

**53 archivos**, todos bajo `frontend/`:

| Grupo | Cantidad | Archivos |
|---|---|---|
| Dependencias y configuración | 4 | `apps/tourist-mobile/package.json`, `package-lock.json`, `apps/tourist-mobile/app.json`, `apps/tourist-mobile/jest.config.js` |
| Código de producción | 5 | `app/(auth)/login.tsx`, `app/(auth)/register.tsx`, `app/assistant/[id].tsx`, `src/features/catalog/CatalogList.tsx`, `src/ui/index.tsx` |
| Tests migrados a RNTL 14 | 44 | todos bajo `src/**/__tests__/` |

Revisión de los diffs de producción: son **exactamente** lo que exige el upgrade, nada más.

- `login.tsx` y `register.tsx` — 2 líneas cada uno: envuelven `onSubmit` porque RN 0.88 ya no
  acepta el tipo que devuelve `handleSubmit` de react-hook-form
- `assistant/[id].tsx` — el ref del `ScrollView` pasa a tiparse con `ScrollViewInstance`
- `CatalogList.tsx` y `ui/index.tsx` — **sólo se agregan `testID`**, porque RNTL 14 eliminó las
  consultas por tipo de componente. Props aditivas: no cambian comportamiento ni layout

### Commit 2 — `docs(oleada19): record functional validation and closure evidence`

**9 archivos**:

| Archivo | Estado |
|---|---|
| `docs/oleada19-validacion-funcional.md` | nuevo |
| `docs/oleada19-informe-cierre.md` | nuevo |
| `docs/plan-staging-turisclick.md` | nuevo |
| `docs/evidencia/oleada19/README.md` | nuevo |
| `docs/evidencia/oleada19/evidencia-o19.json` | nuevo *(redactado)* |
| `docs/evidencia/oleada19/bateria_o19.py` | nuevo *(redactado)* |
| `docs/evidencia/oleada19/run_staging.py` | nuevo *(redactado)* |
| `docs/evidencia/oleada19/s2_aislamiento.py` | nuevo |
| `docs/evidencia/oleada19/staging_db.py` | nuevo |

### ⚠️ Lo que NO entra en ninguno de los dos

Hay **tres cuerpos de trabajo distintos** pendientes en `docs/`, y meterlos en un commit
rotulado `docs(oleada19)` falsearía la historia que después se cita en la tesis:

| Archivos | Qué son | A qué pertenecen |
|---|---|---|
| `docs/use-cases.md` *(+138 líneas)* | Renumeración de `UC-T-19`→`UC-T-23` y 22 secciones de casos de uso que faltaban | Catálogo de casos de uso, fase de reconstrucción del backlog |
| `docs/diagrams/uml/**` *(7 archivos)* | Diagramas regenerados por esa renumeración | Idem |
| `docs/jira-import/` *(10 archivos)* | Los 217 CSV de importación, que cubren las 19 oleadas | Reconstrucción del Scrum en Jira |

Necesitan **su propio commit** (o dos). No los incluí porque vos pediste explícitamente separar
lo técnico de lo académico y que el commit 2 fuera *exclusivamente* de la Oleada 19. Decime cómo
los querés agrupar.

### Comandos, listos para cuando los apruebes

```bash
git add frontend/apps/tourist-mobile frontend/package-lock.json
```

```bash
git commit -m "chore(tourist-mobile): upgrade Expo SDK to 58"
```

```bash
git add docs/oleada19-validacion-funcional.md docs/oleada19-informe-cierre.md docs/plan-staging-turisclick.md docs/evidencia/oleada19
```

```bash
git commit -m "docs(oleada19): record functional validation and closure evidence"
```

**Nada está staged todavía.** El índice está limpio: preparé las listas, no toqué el estado del
repositorio.

---

## 6.5 Pendientes que sólo dependen de datos tuyos

Vienen de antes de esta oleada y bloquean la documentación de grado, no el cierre funcional:

| Dato | Dónde impacta |
|---|---|
| Nombre del CEO de TrendyTrips | `TC-165` / `INV-09` |
| Resultados de las tres entrevistas | `TC-166` / `INV-10` |
| Fechas de entrega y defensa de la tesis | 10 ítems `DOC-` sin sprint |
| Validación de las fechas estimadas de abril–mayo | Cronología del informe |
| Host de Azure V1 | Hallazgo H-03 |

## 6.6 Decisiones técnicas que quedan abiertas

| Tema | Estado |
|---|---|
| Duplicación de React en el monorepo | Preexistente. Se arregla alineando el Backoffice |
| `web.output` del SDK 58 | Cambió su valor por omisión a salida de servidor. La app es móvil y no exporta web, así que no la afecta; si alguna vez se exporta, hay que fijar `"static"` |
| EPERM intermitente de la caché de Jest en Windows | Ambiental. Se resuelve con `npx jest --clearCache` |
| Test intermitente en `AssistantChatHistory.test.tsx` | **Abierto.** Falla ~1 de 8 tests sólo en corrida completa, no en aislamiento. Es la suite más pesada (~93 s). Probable trabajo asíncrono residual entre tests. No bloquea el cierre funcional, pero conviene estabilizarlo antes de apoyarse en la suite como evidencia de regresión |

---

# 7. PROPUESTA DE ACTUALIZACIÓN DE JIRA — no ejecutada

Derivada de `docs/jira-import/00-turisclick-completo.csv`, que es la fuente de lo que se importó.
Los identificadores de abajo son los `Issue id` del CSV. Su correspondencia con las claves
`TC-xxx` que Jira asignó en la importación **ya está documentada** en
[`jira-import/07-sprints-y-claves.md`](jira-import/07-sprints-y-claves.md), así que no hace falta
resolverla leyendo el proyecto:

| ID funcional | `Issue id` | Clave Jira |
|---|---|---|
| `UC-T-25` | 2073 | `TC-90` |
| `UC-T-26` | 2074 | `TC-91` |
| `BUG-10` | 2094 | `TC-111` |
| `PEND-01` | 2164 | `TC-181` |
| `PEND-02` | 2165 | `TC-182` |
| `PEND-03` | 2166 | `TC-183` |
| `PEND-04` | 2167 | `TC-184` |
| `PEND-06` | 2169 | `TC-186` |
| `PEND-07` | 2170 | `TC-187` |
| `PEND-09` | 2172 | `TC-189` |
| `PEND-13` | 2176 | `TC-193` |

Hay **11 ítems etiquetados `oleada-19`**: 3 en *Done* y 8 en *To Do*.

## 7.1 Ya cerrados — no requieren acción

| id | Tipo | Resumen | Estado |
|---|---|---|---|
| 2073 | Story | `[UC-T-25]` Buscar vuelos entre dos aeropuertos | Done |
| 2074 | Story | `[UC-T-26]` Abrir la oferta de vuelo en el sitio del vendedor | Done |
| 2094 | Bug | `[BUG-10]` Se cobraban reservas de vuelo de operador heredadas | Done |

## 7.2 Propuestos para cerrar — la validación los respalda

| id | Resumen | Qué lo respalda |
|---|---|---|
| 2164 | `[PEND-01]` Desplegar el backend V2 con la Oleada 19 en Azure | §1.1 — despliegue confirmado por comportamiento |
| 2165 | `[PEND-02]` Verificar que `redirect` aparece tras el despliegue | §1.1 — campo presente el 09/11, ausente el 27/10 |
| 2167 | `[PEND-04]` Desplegar el Backoffice sin la navegación de Vuelos | §1.3 — A-14 y A-15 aprobadas en Azure |
| 2170 | `[PEND-07]` Validar que los paquetes con vuelo se siguen pagando | T-06 en staging + M-07/M-08 manuales |
| 2176 | `[PEND-13]` Verificar el metabuscador DEMO de punta a punta | M-01 a M-06, reportadas por el usuario |

## 7.3 ⚠️ NO cerrar — su criterio pide Azure y se validó en staging

| id | Resumen | Situación |
|---|---|---|
| 2166 | `[PEND-03]` Verificar que los vuelos de operador responden **501 en Azure** | El 501 se comprobó en **staging** (T-12). En Azure nunca se hizo una petición autenticada a esas rutas |
| 2169 | `[PEND-06]` Validar **en Azure** que una reserva heredada de vuelo no se cobra | Validado en **staging** (T-06). En Azure no se escribió, por decisión tuya |

Son el mismo caso: el comportamiento **está demostrado**, pero no en el entorno que pide el
criterio de aceptación. Hay tres salidas, y es tu decisión:

1. **Reescribir el criterio** para que diga "en un entorno equivalente a producción", y cerrarlos
   citando T-06 y T-12. Es lo que refleja lo que realmente se decidió.
2. **Dejarlos abiertos** y reconocer que requieren escribir en Azure, que descartaste.
3. **Cerrarlos con una nota** que explique la sustitución de entorno.

Recomiendo la 1: el criterio original asumía que se podía escribir en producción, y esa premisa
cambió.

## 7.4 Sin verificar — quedan abiertos

| id | Resumen | Por qué |
|---|---|---|
| 2172 | `[PEND-09]` Configurar `Flights:RedirectHosts` | No lo verifiqué. Es configuración de Azure, que no toqué |

## 7.5 Ítems nuevos a crear

| Tipo | Resumen propuesto | Descripción |
|---|---|---|
| Task | Actualizar Tourist Mobile a Expo SDK 58 | Trabajo técnico **no planificado**, forzado por que Expo Go dejó de soportar el SDK 57 y bloqueaba las 8 pruebas manuales. Incluye RN 0.86→0.88, React 19.2→19.3 y la migración de 44 archivos de test a RNTL 14. Verificado: TypeScript y ESLint sin errores, 528/528 tests, Expo Doctor 19/20 |
| Task | Registrar las pruebas funcionales de la Oleada 19 | 41 pruebas: 15 en Azure V2 (lectura), 25 en staging local, 8 manuales reportadas por el usuario. 40 aprobadas, 0 fallidas, S-5 no ejecutada |
| Bug *(deuda técnica)* | React duplicado en el monorepo del frontend | La app móvil resuelve React 19.3.0 y `react-native`/`expo-router`/NativeWind resuelven 19.3.1. **Preexistente al upgrade**: el lockfile anterior muestra la misma división con 19.2.3/19.2.8. Único check que falla en Expo Doctor. Se arregla alineando el React del Backoffice |

Sobre el ítem del SDK 58: sugiero **no** retrotraerlo a un sprint pasado. Es trabajo del 09–10/11
y ponerlo antes falsearía la cronología.

## 7.6 Relacionados, sin etiqueta `oleada-19`

| id | Resumen | Nota |
|---|---|---|
| 2168 | `[PEND-05]` Ejecutar la suite completa sobre `2d75b5d` en base aislada | **Cumplido de hecho** por la batería en staging. Evaluá si corresponde cerrarlo |
| 2171 | `[PEND-08]` Verificar la migración 0015 en base local y de tests | Staging tiene las 15 migraciones, con `0015_Oleada18_VuelosDeOperador` como última. Cubre la parte local |

---

## 7.7 Seudonimización de los entrevistados

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

> **La Oleada 19 cumple lo que se propuso y está lista para cerrarse.** 40 de 41 pruebas
> aprobadas, ninguna fallida, ningún defecto funcional abierto.

Las dos afirmaciones que importaban quedaron demostradas con evidencia, y en los dos entornos
que correspondía:

- **En Azure V2**, que lo desplegado es la Oleada 19 y que el metabuscador funciona en producción.
- **En staging local**, que apagar la venta de vuelos de operador no rompió los paquetes con
  vuelo — con reserva, pago simulado y cero emisión de pasajes, verificado en base.

**No declaro la oleada cerrada**, porque el cierre formal depende de dos cosas que son tuyas:
versionar el trabajo (§6.1) y autorizar el movimiento en Jira (§6.3). Lo primero es urgente.
