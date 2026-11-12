# Oleada 19 — Validación funcional

Commit validado: **`2d75b5d`** · Última actualización: **10/11/2026**

Dos entornos, y conviene no mezclarlos:

| | |
|---|---|
| **Azure V2** | `https://app-turisclick-v2-api.azurewebsites.net` — **sólo lectura**, nunca se escribió |
| **Staging local** | `http://localhost:5299` + PostgreSQL local `turisclick_staging_o19` — donde se ejecutaron las pruebas que escriben |

---

# RESUMEN

| | |
|---|---|
| **Despliegue en Azure V2** | ✅ **Confirmado** |
| **Verificaciones de sólo lectura en Azure V2** | ✅ **15 aprobadas, 0 fallidas** |
| **Pruebas funcionales en staging local** | ✅ **13 aprobadas, 0 fallidas** |
| **Pruebas de aislamiento** | ✅ **4 aprobadas** · 1 no ejecutada |
| **Pruebas manuales en Expo** | ✅ **8 aprobadas** — *reportadas por el usuario*, ver §4 |
| **¿Oleada 19 cerrable?** | ✅ **Sí, funcionalmente** — ver §9 para lo que resta, que no es funcional |

**Azure V2 no se modificó.** Verificado por contraste: 13 paquetes antes, 13 después, 0 de
prueba.

---

# 1. VERIFICACIONES EN AZURE V2 — sólo lectura · 15 aprobadas

Ejecutadas contra el entorno desplegado sin escribir nada.

## 1.1 El despliegue corresponde a la Oleada 19

La respuesta de `POST /api/flights/search` trae el campo que **sólo existe desde el commit
`66635cc`**:

```json
"redirect": { "available": false, "url": null,
              "sellerName": "Fake Airways", "unavailableReason": "DEMO" }
```

El 27/10, con la Oleada 18 desplegada, ese campo tenía **0 apariciones**.

## 1.2 Metabuscador

| # | Prueba | Resultado | |
|---|---|---|---|
| A-01 | Búsqueda sin sesión | HTTP 200 | ✅ |
| A-02 | Sólo ida, con itinerario | 4 ofertas, 1 tramo VVI→LPB | ✅ |
| A-03 | Ida y vuelta | 2 tramos: VVI→LPB y **LPB→VVI** | ✅ |
| A-04 | Rótulo de demostración | `testMode: true`, `inventoryMode: "SIMULATED"` | ✅ |
| A-05 | Redirección en DEMO | `available: false`, `unavailableReason: "DEMO"` | ✅ |
| A-06 | Vendedor informado | `sellerName: "Fake Airways"` | ✅ |
| A-07 | **Nada reservable** | **0** `quoteId`, **0** `offerId` | ✅ |
| A-08 | **Ninguna oferta comprable** | **5/5** `bookable: false` | ✅ |
| A-09 | Sin vuelos de operador | `providerFlight: null` | ✅ |
| A-10 | Origen = destino | HTTP 400 | ✅ |
| A-11 | Aeropuerto inexistente | HTTP 400 | ✅ |
| A-12 | Fecha pasada | HTTP 400 | ✅ |
| A-13 | Regreso antes de la ida | HTTP 400 | ✅ |

## 1.3 Backoffice desplegado

Inspección del bundle de producción `/assets/index-pCiBjldm.js` (733.407 bytes).

| # | Prueba | Resultado | |
|---|---|---|---|
| A-14 | **Sin menú de vuelos** | `"Vuelos"` → **0 apariciones** | ✅ |
| A-15 | **`/admin/flights`** | **Redirige a `/dashboard`** | ✅ |

Ausentes también: `providerFlight`, `canSellFlights`, `flightSeller`, `FLIGHT_SELLER`.

> Las etiquetas *"Emisión pendiente"*, *"Emitido"* y *"Emisión fallida"* siguen en el
> bundle. **No es un resto de la Oleada 18**: son los estados de emisión del vuelo **dentro
> de un paquete** (UC-T-21), vigente.

---

# 2. PRUEBAS FUNCIONALES EN STAGING LOCAL — 13 aprobadas

Entorno levantado para esto, con base, clave de firma y proveedor propios.

| Elemento | Valor |
|---|---|
| API | `http://localhost:5299` |
| Base | `localhost:5432/turisclick_staging_o19` — **creada para esta validación** |
| Esquema | **15 migraciones**, última `0015_Oleada18_VuelosDeOperador` |
| Proveedor aéreo | **Fake** · `inventoryMode: "SIMULATED"` |
| Pasarela | **SimulatedPaymentGateway** — única existente |
| Clave JWT | **propia**, distinta de desarrollo y de producción |

## 2.1 Resultados

| # | Prueba | Caso de uso | Resultado |
|---|---|---|---|
| T-01 | Cotizar el vuelo del paquete desde VVI | UC-T-23 | ✅ HTTP 200 · **3 ofertas** |
| T-02 | Un origen **no** permitido se rechaza | UC-P-15 | ✅ HTTP 400 |
| T-03 | Revalidar la cotización | UC-T-20 | ✅ `outcome: UNCHANGED` |
| T-04 | **Reservar paquete + vuelo** en una operación | UC-T-21 | ✅ HTTP 201 · vuelo `PENDING` |
| T-05 | La misma cotización no crea una segunda reserva | UC-T-18 | ✅ devuelve la misma |
| **T-06** | **El paquete con vuelo SÍ se cobra** | **regresión BUG-10** | ✅ **`paymentApproved: true`** |
| T-07 | La reserva queda `CONFIRMED` | UC-SYS-07 | ✅ reserva y vuelo `CONFIRMED` |
| T-08 | La búsqueda devuelve el paquete de la ruta | UC-T-25 | ✅ aparece en VVI→LPB |
| T-09 | Presupuesto de cancelación por componente | UC-T-22 | ✅ líneas `PACKAGE` y `FLIGHT` |
| T-10 | Cancelación ejecutada | UC-T-11 | ✅ `COMPLETED` |
| T-11 | Libro de pagos del administrador | UC-A-10 | ✅ HTTP 200 |
| **T-12** | **Vuelos de operador responden 501** | **B-04** | ✅ operador **501** · admin **501** |
| **T-13** | **Empresa suspendida: reservar con un id conocido** | **UC-A-08** | ✅ **HTTP 404** |

## 2.2 Las tres que más importan

**T-06 — la regresión de BUG-10.** Es la pregunta que cierra la Oleada 19: al apagar los
vuelos de operador, ¿se siguen cobrando los paquetes con vuelo? **Sí.** El pago devolvió
`paymentApproved: true` y la reserva quedó `CONFIRMED` con el vuelo también confirmado.

**T-12 — el 501.** Las dos rutas de vuelos de operador —`GET /api/companies/me/flights` del
operador y `GET /api/admin/flights` del administrador— responden **501
`FEATURE_NOT_AVAILABLE`** con sesión válida. Anónimamente sólo se veía 401, que no probaba
nada.

**T-13 — la puerta de atrás.** Se creó una salida, se **suspendió la empresa**, y se intentó
reservar **con el `availabilityId` ya conocido**: **404**. Demuestra que la suspensión no
depende del estado de publicación ni de que el id sea secreto.

---

# 3. PRUEBAS DE AISLAMIENTO — 4 aprobadas, 1 no ejecutada

| # | Prueba | Resultado | |
|---|---|---|---|
| **S-1** | Catálogos distintos | staging **2** · Azure **13** | ✅ |
| **S-2** | **Token de staging contra Azure V2** | staging **200** · Azure **401** | ✅ |
| **S-3** | Azure antes y después de la batería | **13 = 13**, 0 paquetes de prueba | ✅ |
| **S-4** | Proveedor en staging | `inventoryMode: "SIMULATED"` | ✅ |
| S-5 | Captura de tráfico saliente durante el pago | **no ejecutada** | ⬜ |

## 3.1 S-2 es la importante

`Issuer` y `Audience` son **idénticos en todos los ambientes** (`TurisClick.Api` y
`TurisClick.Client`, literales en `appsettings.json`): **no aíslan nada**. El único
aislamiento real es la **clave de firma**, porque `Program.cs` valida con
`ValidateIssuerSigningKey = true`.

La prueba lo demuestra: el mismo token devuelve **200 en staging** y **401 en Azure V2**.

## 3.2 S-5 no se ejecutó, y no hace falta para afirmar lo que afirma

No capturé el tráfico saliente. Lo que sí está verificado, en código:

- **`SimulatedPaymentGateway` es la única implementación de `IPaymentGateway`** en todo el
  repositorio, registrada incondicionalmente en `ReservationsModuleExtensions.cs:26`.
- `ChargeAsync` decide con un booleano que manda el cliente. Sin SDK, sin tarjeta, sin red.
- El proveedor aéreo es el **falso**: `inventoryMode: "SIMULATED"`.

**Cero cargos externos, cero pasajes emitidos.** Pero conviene decir que es evidencia de
código, no de red.

---

# 4. PRUEBAS MANUALES EN EXPO — 8 aprobadas

> **Origen del resultado: reportado por el usuario.** No las observé yo. Se ejecutaron el
> **10/11/2026** en un iPhone físico, con **Expo Go sobre el SDK 58**, apuntando al staging
> local. **No hay capturas ni grabaciones**: el registro es el reporte del usuario, y queda
> asentado como tal. Las instrucciones seguidas son las de la sección 6.

| # | Prueba | Resultado |
|---|---|---|
| M-01 | La app conecta con el backend de staging | ✅ Aprobada *(reportada)* |
| M-02 | Búsqueda DEMO sólo ida e ida y vuelta | ✅ Aprobada *(reportada)* |
| M-03 | Las ofertas se rotulan como de demostración | ✅ Aprobada *(reportada)* |
| M-04 | No hay ninguna ruta para comprar un vuelo suelto | ✅ Aprobada *(reportada)* |
| M-05 | *"Ver oferta en {vendedor}"* ausente o inhabilitado en DEMO | ✅ Aprobada *(reportada)* |
| M-06 | Separación visible entre metabuscador y paquetes | ✅ Aprobada *(reportada)* |
| M-07 | Reserva del paquete con vuelo desde la app | ✅ Aprobada *(reportada)* |
| M-08 | El pago es simulado y no hay emisión de pasajes | ✅ Aprobada *(reportada)* |

## 4.1 Corroboración indirecta en la base de staging

M-07 y M-08 escriben, así que dejan rastro. Leí la base **en sólo lectura** después del
reporte, y lo que hay es consistente con que se ejecutaron:

| Observación | Valor | Qué respalda |
|---|---|---|
| Reservas creadas el **10/11 00:01 y 00:02** | 2, ambas `CONFIRMED`, 1 ítem cada una | M-07 reservó y M-08 confirmó |
| Transacciones de pago de esas dos reservas | `SUCCEEDED`, proveedor **`Simulated`** | M-08: el pago es simulado |
| `provider_flights` | **0 filas** | No hubo vuelo de operador: la funcionalidad sigue apagada |
| `reservation_passengers` | **0 filas** | **No se emitió ningún pasaje** |
| `flight_quotes` / `flight_bookings` | 18 / 3 | La cotización de vuelo del paquete funcionó |
| Reserva anterior, del **09/11 22:03** | 1, `CANCELLED` | Es de la batería automática T-01..T-13, no de las manuales |

**Esto no prueba que las ocho pruebas pasaran** — el juicio sobre lo que se vio en pantalla es
del usuario, y las seis primeras (M-01 a M-06) son observaciones de interfaz que no dejan
rastro en la base. Lo que sí queda demostrado por datos es que **M-07 y M-08 se ejecutaron**,
que el cobro pasó por el gateway simulado y que **no hubo emisión de pasajes**.

---
# 5. ERRORES ENCONTRADOS

**Ningún defecto funcional.** Las 13 pruebas locales y las 15 de Azure pasaron.

| # | Hallazgo | Gravedad | Estado |
|---|---|---|---|
| H-01 | Ningún paquete de Azure V2 tiene vuelo configurado | Media | **Resuelto por otra vía**: la validación se hizo en staging, sin tocar producción |
| H-02 | `Issuer` y `Audience` iguales en todos los ambientes | **Informativa** | No es un defecto, pero **obliga** a que cada ambiente tenga su propia clave de firma. Verificado en S-2 |
| H-03 | El host de Azure V1 no está documentado | Baja | Abierto. Impide verificar V1 de forma independiente |

**Sobre Azure V1**: no se ejecutó ni una sola petición contra él. Las únicas URL consultadas
fueron la API de V2, el Backoffice de V2 y `localhost:5299`.

---

# 6. INSTRUCCIONES PARA LAS 8 PRUEBAS DE EXPO

## 6.1 Qué ya está listo

| | |
|---|---|
| API de staging | **corriendo** en `http://0.0.0.0:5299` |
| Configuración de Expo | **`frontend/apps/tourist-mobile/.env.local` ya creado** |
| IP de la PC en la LAN | **`192.168.0.5`** |

> El `.env` original **no se tocó**: sigue apuntando a Azure. `.env.local` tiene prioridad y
> está en `.gitignore`.
> Si tu IP cambió, editá ese archivo.

## 6.2 Paquete de prueba

| | |
|---|---|
| Título | `[PRUEBA TECNICA · NO RESERVAR] Validacion Oleada 19 276185` |
| Id | `683510e6-a238-45ed-86a1-c49292af8bf3` |
| Vuelo | **VVI → LPB**, ida y vuelta, cabina ECONOMY |
| Salidas con cupo | **24/12/2026** y **08/01/2027**, 1 cupo cada una |

Cuenta de turista usada: **`qa.turista.276185@turisclick.dev`** · contraseña **no versionada**.

*Cuenta ficticia de un entorno local sin datos reales. La contraseña se omitió de este documento a
propósito: el repositorio tiene remoto en GitHub. El valor quedó en la copia privada de la
evidencia, fuera del repositorio, junto con el script que la usó.*

## 6.3 Arrancar

```bash
cd frontend/apps/tourist-mobile
npx expo start --clear
```

El `--clear` importa: sin él, Expo puede reusar un bundle con la URL vieja.

Escaneá el QR con Expo Go. **El teléfono tiene que estar en la misma Wi-Fi.**

Si la app no conecta, lo más probable es el **firewall de Windows** bloqueando el 5299.
Para abrirlo, en PowerShell **como administrador**:

```powershell
New-NetFirewallRule -DisplayName "TurisClick staging 5299" -Direction Inbound -LocalPort 5299 -Protocol TCP -Action Allow
```

Para comprobar antes, desde el navegador **del teléfono**: `http://192.168.0.5:5299/api/packages`
debería devolver JSON con 2 paquetes.

## 6.4 Qué observar en cada prueba

| # | Qué hacer | Qué tiene que pasar |
|---|---|---|
| **M-01** | Abrir la app | Carga el catálogo con **2 paquetes de prueba**, no los 13 de Azure. Si ves 13, está apuntando a producción |
| **M-02** | Vuelos: VVI → LPB, buscar sólo ida. Repetir con fecha de regreso | Ambas devuelven ofertas; la de ida y vuelta muestra **dos tramos** |
| **M-03** | Mirar las ofertas | Rotuladas como demostración, con un texto del tipo *"no se puede comprar"* |
| **M-04** | Intentar comprar un vuelo suelto | **No existe ningún botón de reservar ni comprar** |
| **M-05** | Buscar *"Ver oferta en…"* | **Ausente o inhabilitado**: en DEMO ninguna oferta redirige |
| **M-06** | Volver al inicio | Metabuscador y paquetes son secciones **separadas** |
| **M-07** | Abrir el paquete de prueba → reservar con vuelo desde VVI | Cotiza, muestra precio del vuelo y total, y permite reservar |
| **M-08** | Pagar la reserva | El pago es **simulado**; la reserva queda confirmada; **no aparece ningún pasaje emitido real** |

**M-07 y M-08 escriben en staging**, nunca en Azure. Es el mismo flujo que T-04 y T-06
validaron por API.

## 6.5 Al terminar

1. **Borrar `frontend/apps/tourist-mobile/.env.local`** — si queda, la app buscará un
   backend que ya no está.
2. Avisame y yo detengo la API y borro la base de staging.

---

# 7. LIMPIEZA Y RESIDUOS

| Elemento | Estado |
|---|---|
| Base `turisclick_staging_o19` | **Local, intacta y conservada.** 33 tablas · 15 migraciones · 2 paquetes · 3 reservas · 5 pagos simulados. **No borrar hasta volcar la evidencia (§8)** |
| API en el puerto 5299 | **Detenida** — el proceso terminó al cerrarse la sesión del 09/11. Verificado: no responde |
| `.env.local` de Expo | **Todavía presente.** Ya se puede borrar: las pruebas terminaron |
| `.env` de Expo y Backoffice | **No se tocaron** |
| Bases `turisclick_v2_dev` y `turisclick_v2_test` | **No se tocaron** — dev sigue en 14 migraciones y 127 usuarios |
| Azure V1 y V2 | **No se tocaron** |
| Código, pipelines, Jira | **No se tocaron** |

**Residuo conocido**: la primera corrida de la batería se interrumpió por un problema de
codificación de consola y dejó **un paquete de prueba extra** en staging. Por eso hay 2 y no
1. Es local y desaparece al borrar la base.

---

# 8. EVIDENCIA

| Elemento | Dónde |
|---|---|
| Respuestas JSON de los 13 pasos | `evidencia-o19.json` en el scratchpad de la sesión |
| Id del paquete de prueba | `683510e6-a238-45ed-86a1-c49292af8bf3` |
| Esquema de staging | 15 migraciones · `0015_Oleada18_VuelosDeOperador` · 33 tablas |
| S-2 | staging **200** / Azure **401** con el mismo token |
| S-3 | Azure **13 = 13** paquetes, **0** de prueba |
| Marcador de despliegue | campo `redirect` presente en Azure *(0 apariciones el 27/10)* |
| Rastro de M-07 y M-08 | 2 reservas `CONFIRMED` del 10/11 00:01–00:02 con pago `Simulated` · §4.1 |
| Ausencia de emisión | `provider_flights` y `reservation_passengers` en **0** filas · §4.1 |

## 8.1 Riesgo: la evidencia automática vive en un directorio temporal

`evidencia-o19.json` y los scripts de la batería están en el **scratchpad de la sesión**, que es
un directorio temporal del sistema y no sobrevive indefinidamente. Lo mismo vale para la base
de staging, que es local y no está respaldada.

Para la documentación de grado conviene, **antes de borrar staging**:

1. Copiar `evidencia-o19.json` y los scripts (`bateria_o19.py`, `s2_aislamiento.py`,
   `run_staging.py`, `staging_db.py`) a una carpeta versionada del repositorio.
2. Volcar de la base los conteos de §4.1 a un archivo de texto, que es lo único que no se
   puede reconstruir una vez borrada.

Hecho eso, borrar la base no pierde nada.

---

# 9. RECOMENDACIÓN

> ## ✅ La Oleada 19 **está funcionalmente validada**.

Lo que quedaba pendiente eran las ocho pruebas de interfaz, y el usuario las reportó aprobadas
el 10/11/2026 sobre Expo SDK 58 (§4). Con eso, el cuadro completo es:

| Entorno | Pruebas | Resultado | Quién las ejecutó |
|---|---|---|---|
| **Azure V2** (sólo lectura) | 15 | ✅ 15 aprobadas | Yo, por API |
| **Staging local** (automáticas) | 13 | ✅ 13 aprobadas | Yo, por API |
| **Staging local** (aislamiento) | 5 | ✅ 4 aprobadas · 1 no ejecutada | Yo, por API |
| **Staging local** (manuales, Expo) | 8 | ✅ 8 aprobadas | **El usuario, en su iPhone** |
| | **41** | **40 aprobadas · 1 no ejecutada · 0 fallidas** | |

Lo sustantivo quedó demostrado:

- **El despliegue es el commit correcto** — verificado por comportamiento en Azure.
- **El metabuscador se comporta como debe** — en Azure y, ahora, también en la interfaz real.
- **Apagar los vuelos de operador no rompió los paquetes con vuelo** — T-06 automática y
  M-07/M-08 manuales, con rastro en base y sin emisión de pasajes.
- **La funcionalidad apagada está efectivamente apagada** — T-12 y `provider_flights` en 0.
- **La suspensión de empresa cierra la puerta de atrás** — T-13.

**Lo que falta para el cierre formal no es funcional**: es versionar el trabajo, volcar la
evidencia y recién entonces desarmar staging. Está detallado en el informe de cierre
(`oleada19-informe-cierre.md`).
