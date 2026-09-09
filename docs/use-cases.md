# TurisClick — Catálogo de Casos de Uso

Este documento es la fuente de verdad funcional del proyecto a partir de FASE 1. Cada módulo del backend (FASE 5) se implementará siguiendo el patrón:

```
CASO DE USO → diseño funcional → entidades necesarias → DTOs → Repository → Service → Controller/endpoints → migración → pruebas → Postman → revisión → siguiente caso de uso
```

## Decisiones funcionales ya aprobadas (contexto para todos los casos de uso)

1. **Provider Package** puede combinar `Experience` reales y reutilizables (vendibles también por separado) con **ítems descriptivos propios del paquete** (traslado, desayuno, tiempo libre) que no existen como producto independiente.
2. **AI Itinerary**: al reservarse genera una **reserva padre** (visible para el turista como un solo viaje) y **reservas hijas** por producto/proveedor (cada proveedor solo ve su parte).
3. La IA puede combinar `Experience` sueltas, `Provider Package` completos, o ambos, dentro de un mismo itinerario.
4. Reserva y pago están desacoplados: una reserva puede existir en estado `PENDING_PAYMENT` y pasar a `CONFIRMED` cuando el pago se resuelve. La pasarela de pago real se implementa más adelante; por ahora existe el estado y el punto de extensión.
5. `Company` se diseña para soportar múltiples usuarios `PROVIDER` en el futuro, aunque la primera implementación asuma uno solo.
6. La confirmación de reserva es **automática** si hay cupo disponible y el pago es exitoso — no hay aceptación manual del proveedor.
7. `AI Itinerary` se **persiste**: el turista puede guardarlo, retomarlo y seguir modificándolo antes de comprarlo.
8. Disponibilidad se modela con **fechas/slots concretos** (no recurrencia todavía), dejando el diseño abierto a evolucionar hacia reglas recurrentes.
9. Cancelaciones/reembolsos se incluyen en el **modelo conceptual** (estados, relaciones) pero no son prioridad de implementación temprana.
10. Destinos usan una jerarquía simple: **Country → Region/Department/State → City/Destination**.

## Convención de IDs

- `UC-AUTH-xx`: autenticación y cuenta (compartido entre roles)
- `UC-T-xx`: casos de uso del actor **TOURIST**
- `UC-P-xx`: casos de uso del actor **PROVIDER**
- `UC-A-xx`: casos de uso del actor **ADMIN**
- `UC-AI-xx`: casos de uso del actor de sistema **AI AGENT**
- `UC-SYS-xx`: casos de uso internos del sistema (sin actor humano directo, invocados por otros casos de uso)

---

## Tabla resumen

| ID | Nombre | Actor principal | Oleada |
|---|---|---|---|
| UC-AUTH-01 | Registrar cuenta de Turista | TOURIST | 0 |
| UC-AUTH-02 | Iniciar sesión | Cualquier usuario | 0 |
| UC-AUTH-03 | Refrescar token de sesión | Cualquier usuario | 0 |
| UC-AUTH-04 | Cerrar sesión | Cualquier usuario | 0 |
| UC-SYS-03 | Validar propiedad de una empresa | Sistema | 0 |
| UC-A-04 | Gestionar destinos | ADMIN | 1 |
| UC-A-05 | Gestionar categorías | ADMIN | 1 |
| UC-P-01 | Registrar empresa y solicitar cuenta Provider | PROVIDER | 1 |
| UC-A-01 | Revisar solicitudes de empresa pendientes | ADMIN | 1 |
| UC-A-02 | Aprobar solicitud de empresa | ADMIN | 1 |
| UC-A-03 | Rechazar solicitud de empresa | ADMIN | 1 |
| UC-P-02 | Gestionar perfil de "Mi Empresa" | PROVIDER | 1 |
| UC-P-04 | Crear experiencia | PROVIDER | 2 |
| UC-P-05 | Editar experiencia | PROVIDER | 2 |
| UC-P-06 | Publicar/despublicar experiencia | PROVIDER | 2 |
| UC-P-10 | Definir disponibilidad de una experiencia | PROVIDER | 2 |
| UC-T-03 | Explorar destinos | TOURIST | 2 |
| UC-T-04 | Buscar/filtrar experiencias | TOURIST | 2 |
| UC-T-05 | Ver detalle de una experiencia | TOURIST | 2 |
| UC-T-08 | Reservar una experiencia individual | TOURIST | 2 |
| UC-SYS-01 | Verificar disponibilidad y cupos | Sistema | 2 |
| UC-SYS-04 | Crear reserva simple | Sistema | 2 |
| UC-SYS-06 | Retener/descontar cupo de forma atómica | Sistema | 2 |
| UC-T-10 | Ver mis reservas | TOURIST | 2 |
| UC-P-12 | Ver reservas recibidas | PROVIDER | 2 |
| UC-P-13 | Ver detalle de una reserva | PROVIDER | 2 |
| UC-T-19 | Pagar una reserva pendiente | TOURIST | 3 |
| UC-SYS-07 | Confirmar reserva tras pago exitoso | Sistema | 3 |
| UC-SYS-02 | Recalcular/revalidar precio | Sistema | 3 |
| UC-P-07 | Crear paquete | PROVIDER | 4 |
| UC-P-08 | Editar paquete | PROVIDER | 4 |
| UC-P-09 | Publicar/despublicar paquete | PROVIDER | 4 |
| UC-P-11 | Definir disponibilidad de un paquete | PROVIDER | 4 |
| UC-T-06 | Buscar/filtrar paquetes | TOURIST | 4 |
| UC-T-07 | Ver detalle de un paquete | TOURIST | 4 |
| UC-T-09 | Reservar un paquete de proveedor | TOURIST | 4 |
| UC-T-12 | Iniciar conversación con TurisClick AI | TOURIST | 5 |
| UC-T-13 | Enviar preferencias de viaje a la IA | TOURIST | 5 |
| UC-AI-01 | Interpretar preferencias de viaje | AI AGENT | 5 |
| UC-AI-02 | Recuperar oferta relevante del catálogo | AI AGENT | 5 |
| UC-AI-03 | Priorizar Provider Package que encaje | AI AGENT | 5 |
| UC-AI-04 | Componer itinerario combinando Experiences/Packages | AI AGENT | 5 |
| UC-T-14 | Ver propuesta de itinerario | TOURIST | 5 |
| UC-T-15 | Modificar/iterar itinerario propuesto | TOURIST | 6 |
| UC-AI-05 | Ajustar itinerario ante nueva instrucción | AI AGENT | 6 |
| UC-AI-06 | Explicar/justificar la propuesta | AI AGENT | 6 |
| UC-T-16 | Guardar itinerario IA | TOURIST | 6 |
| UC-T-17 | Retomar itinerario IA guardado | TOURIST | 6 |
| UC-T-18 | Aceptar y reservar itinerario IA completo | TOURIST | 7 |
| UC-SYS-05 | Crear reservas múltiples desde un itinerario IA | Sistema | 7 |
| UC-SYS-09 | Reindexar catálogo para recuperación (RAG) | Sistema | 7 |
| UC-A-06 | Gestionar usuarios | ADMIN | 8 |
| UC-A-07 | Suspender/despublicar contenido | ADMIN | 8 |
| UC-A-08 | Suspender una empresa | ADMIN | 8 |
| UC-T-11 | Cancelar una reserva | TOURIST | 8 |
| UC-P-14 | Cancelar/rechazar una reserva (excepcional) | PROVIDER | 8 |
| UC-SYS-08 | Liberar cupo por expiración/cancelación | Sistema | 8 |
| UC-P-03 | Agregar usuario Provider adicional a la empresa | PROVIDER | Backlog |

---

## AUTENTICACIÓN Y CUENTA (compartido)

### UC-AUTH-01 — Registrar cuenta de Turista

- **Actor principal:** TOURIST (usuario anónimo que se registra)
- **Actores secundarios:** —
- **Objetivo:** Crear una cuenta de usuario con rol TOURIST para poder explorar y reservar.
- **Precondiciones:** El email no debe existir previamente en el sistema.
- **Flujo principal:**
  1. El usuario envía nombre, email y contraseña.
  2. El sistema valida el formato y unicidad del email.
  3. El sistema crea el `User` con rol `TOURIST`.
  4. El sistema devuelve un JWT de acceso (y refresh token).
- **Flujos alternativos:** —
- **Excepciones:** Email ya registrado → 409; contraseña no cumple política mínima → 400.
- **Postcondiciones:** Usuario creado y autenticado.
- **Reglas de negocio relacionadas:** El registro de TOURIST es autoservicio, sin aprobación.
- **Entidades involucradas:** `User`
- **Endpoints probables:** `POST /api/auth/register`

### UC-AUTH-02 — Iniciar sesión

- **Actor principal:** Cualquier usuario (TOURIST, PROVIDER, ADMIN)
- **Objetivo:** Autenticarse y obtener un JWT válido con el rol correspondiente.
- **Precondiciones:** Cuenta existente y activa.
- **Flujo principal:**
  1. El usuario envía email y contraseña.
  2. El sistema valida credenciales.
  3. El sistema emite `access_token` (JWT con claims de rol y, si aplica, `companyId`) y `refresh_token`.
- **Excepciones:** Credenciales inválidas → 401; usuario suspendido → 403.
- **Postcondiciones:** Sesión iniciada.
- **Reglas de negocio relacionadas:** Un PROVIDER cuya empresa aún no fue aprobada puede iniciar sesión pero con acceso restringido (solo ver estado de su solicitud).
- **Entidades involucradas:** `User`, `Company` (si rol PROVIDER)
- **Endpoints probables:** `POST /api/auth/login`

### UC-AUTH-03 — Refrescar token de sesión

- **Actor principal:** Cualquier usuario autenticado
- **Objetivo:** Obtener un nuevo `access_token` sin reautenticar con credenciales.
- **Precondiciones:** `refresh_token` válido y no expirado/revocado.
- **Flujo principal:** 1. Enviar refresh token. 2. Sistema valida y emite nuevo access token.
- **Excepciones:** Refresh token expirado o revocado → 401.
- **Postcondiciones:** Nuevo access token emitido.
- **Entidades involucradas:** `User`
- **Endpoints probables:** `POST /api/auth/refresh`

### UC-AUTH-04 — Cerrar sesión

- **Actor principal:** Cualquier usuario autenticado
- **Objetivo:** Invalidar el refresh token activo.
- **Flujo principal:** 1. Usuario solicita logout. 2. Sistema revoca el refresh token.
- **Postcondiciones:** Sesión cerrada.
- **Entidades involucradas:** `User`
- **Endpoints probables:** `POST /api/auth/logout`

---

## TOURIST

### UC-T-03 — Explorar destinos

- **Actor principal:** TOURIST
- **Objetivo:** Navegar la jerarquía Country → Region → City para descubrir dónde hay oferta.
- **Precondiciones:** Ninguna (endpoint público).
- **Flujo principal:** 1. Usuario solicita lista de países/regiones/ciudades. 2. Sistema devuelve jerarquía con conteo de experiencias/paquetes activos por nodo.
- **Postcondiciones:** —
- **Entidades involucradas:** `Destination`
- **Endpoints probables:** `GET /api/destinations`, `GET /api/destinations/{id}`

### UC-T-04 — Buscar/filtrar experiencias

- **Actor principal:** TOURIST
- **Objetivo:** Encontrar experiencias por destino, categoría, rango de precio, fecha o texto libre.
- **Precondiciones:** Ninguna (público).
- **Flujo principal:** 1. Usuario aplica filtros. 2. Sistema consulta experiencias `PUBLISHED` que cumplan filtros y tengan disponibilidad futura. 3. Devuelve resultados paginados.
- **Flujos alternativos:** Sin filtros → devuelve catálogo general paginado/ordenado por relevancia.
- **Entidades involucradas:** `Experience`, `Destination`, `Category`, `ExperienceAvailability`
- **Endpoints probables:** `GET /api/experiences?destination=&category=&priceMin=&priceMax=&date=`

### UC-T-05 — Ver detalle de una experiencia

- **Actor principal:** TOURIST
- **Objetivo:** Consultar toda la información necesaria para decidir reservar.
- **Flujo principal:** 1. Usuario abre una experiencia. 2. Sistema devuelve datos completos: proveedor, descripción, incluye/excluye, precio, duración, imágenes, próximas fechas con cupos.
- **Excepciones:** Experiencia no existe o no está `PUBLISHED` → 404.
- **Entidades involucradas:** `Experience`, `Company`, `ExperienceAvailability`
- **Endpoints probables:** `GET /api/experiences/{id}`

### UC-T-06 — Buscar/filtrar paquetes

- **Actor principal:** TOURIST
- **Objetivo:** Encontrar paquetes de proveedor por destino, categoría, precio o duración.
- **Flujo principal:** Análogo a UC-T-04 pero sobre `Package`.
- **Entidades involucradas:** `Package`, `Destination`, `Category`, `PackageAvailability`
- **Endpoints probables:** `GET /api/packages?...`

### UC-T-07 — Ver detalle de un paquete

- **Actor principal:** TOURIST
- **Objetivo:** Ver el itinerario día a día del paquete, precio total, qué incluye/excluye y próximas salidas.
- **Flujo principal:** 1. Usuario abre un paquete. 2. Sistema devuelve estructura por días (cada día con sus `PackageItem`, que pueden ser una `Experience` referenciada o un ítem descriptivo propio del paquete).
- **Excepciones:** Paquete no existe o no está `PUBLISHED` → 404.
- **Entidades involucradas:** `Package`, `PackageItem`, `Experience`, `Company`, `PackageAvailability`
- **Endpoints probables:** `GET /api/packages/{id}`

### UC-T-08 — Reservar una experiencia individual

- **Actor principal:** TOURIST
- **Actores secundarios:** Sistema (verificación de disponibilidad/precio)
- **Objetivo:** Reservar una experiencia para una fecha/slot y número de personas.
- **Precondiciones:** Usuario autenticado; experiencia `PUBLISHED`; slot con cupo suficiente.
- **Flujo principal:**
  1. Usuario elige experiencia, fecha/slot y número de personas.
  2. Sistema ejecuta UC-SYS-01 (verificar disponibilidad) y UC-SYS-02 (revalidar precio).
  3. Sistema ejecuta UC-SYS-04 (crear reserva) en estado `PENDING_PAYMENT`, reteniendo el cupo (UC-SYS-06).
  4. Sistema devuelve la reserva creada.
- **Flujos alternativos:** —
- **Excepciones:** Sin cupo suficiente → 409; slot ya no existe/expiró → 410; precio cambió (informativo, no bloqueante) → se informa antes de confirmar.
- **Postcondiciones:** Reserva creada en `PENDING_PAYMENT`; cupo retenido temporalmente.
- **Reglas de negocio relacionadas:** Regla 3 y 4 del documento de decisiones.
- **Entidades involucradas:** `Reservation`, `ReservationItem`, `Experience`, `ExperienceAvailability`
- **Endpoints probables:** `POST /api/reservations` (`type: EXPERIENCE`)

### UC-T-09 — Reservar un paquete de proveedor

- **Actor principal:** TOURIST
- **Objetivo:** Reservar un paquete completo para una fecha de salida.
- **Precondiciones:** Usuario autenticado; paquete `PUBLISHED`; salida con cupo suficiente.
- **Flujo principal:** Análogo a UC-T-08 pero sobre `Package`/`PackageAvailability`.
- **Excepciones:** Igual que UC-T-08.
- **Postcondiciones:** Reserva creada en `PENDING_PAYMENT`.
- **Entidades involucradas:** `Reservation`, `ReservationItem`, `Package`, `PackageAvailability`
- **Endpoints probables:** `POST /api/reservations` (`type: PACKAGE`)

### UC-T-10 — Ver mis reservas

- **Actor principal:** TOURIST
- **Objetivo:** Consultar historial y estado de las reservas propias (individuales, de paquete o itinerarios IA).
- **Flujo principal:** 1. Usuario solicita su listado. 2. Sistema devuelve reservas propias con estado, y si es una reserva de itinerario IA, agrupa sus `ReservationItem` hijos.
- **Entidades involucradas:** `Reservation`, `ReservationItem`
- **Endpoints probables:** `GET /api/reservations/me`, `GET /api/reservations/{id}`

### UC-T-11 — Cancelar una reserva

- **Actor principal:** TOURIST
- **Objetivo:** Cancelar una reserva propia dentro de la política permitida.
- **Precondiciones:** Reserva en estado cancelable (`PENDING_PAYMENT` o `CONFIRMED` antes de la fecha límite).
- **Flujo principal:** 1. Usuario solicita cancelar. 2. Sistema valida política de cancelación. 3. Sistema pasa la reserva a `CANCELLED` y libera cupo (UC-SYS-08).
- **Excepciones:** Fuera de plazo de cancelación → 409.
- **Postcondiciones:** Reserva `CANCELLED`; cupo liberado.
- **Reglas de negocio relacionadas:** Decisión 9 — modelo conceptual presente, política de reembolso no prioritaria.
- **Entidades involucradas:** `Reservation`
- **Endpoints probables:** `POST /api/reservations/{id}/cancel`
- **Prioridad:** Oleada 8 (no bloqueante para las primeras oleadas).

### UC-T-12 — Iniciar conversación con TurisClick AI

- **Actor principal:** TOURIST
- **Objetivo:** Abrir una sesión conversacional para planificar un viaje.
- **Precondiciones:** Usuario autenticado.
- **Flujo principal:** 1. Usuario inicia conversación. 2. Sistema crea `AiConversation` vacía asociada al turista.
- **Postcondiciones:** Conversación creada.
- **Entidades involucradas:** `AiConversation`
- **Endpoints probables:** `POST /api/ai/conversations`

### UC-T-13 — Enviar preferencias de viaje a la IA

- **Actor principal:** TOURIST
- **Actores secundarios:** AI AGENT
- **Objetivo:** Comunicar en lenguaje natural qué viaje quiere (destino, fechas, presupuesto, intereses, etc.).
- **Precondiciones:** Conversación existente (UC-T-12).
- **Flujo principal:**
  1. Usuario envía un mensaje de texto.
  2. Sistema persiste el `AiMessage`.
  3. Sistema invoca UC-AI-01 → UC-AI-04 para generar/actualizar una propuesta.
  4. Sistema devuelve la propuesta (ver UC-T-14).
- **Entidades involucradas:** `AiConversation`, `AiMessage`, `TripPreferences`
- **Endpoints probables:** `POST /api/ai/conversations/{id}/messages`

### UC-T-14 — Ver propuesta de itinerario

- **Actor principal:** TOURIST
- **Objetivo:** Revisar el itinerario propuesto por la IA (día a día, componentes, precio estimado).
- **Flujo principal:** 1. Sistema devuelve el `AiItinerary` vigente de la conversación, con sus `AiItineraryItem` (cada uno referenciando una `Experience` o un `Package` real).
- **Implementación (Oleada 6):** la respuesta se revalida contra el catálogo vigente antes de devolverse. El snapshot persistido (`EstimatedUnitPrice`/`Currency`) **nunca se reescribe**; el estado actual viaja al lado por DTO (`currentPrice`, `currentCurrency`, `currentAvailableSlots`, `priceChanged`, `availabilityState`, `warnings`).
- **Entidades involucradas:** `AiItinerary`, `AiItineraryItem`, `Experience`, `Package`
- **Endpoints probables:** `GET /api/ai/conversations/{id}/itinerary`

### UC-T-15 — Modificar/iterar itinerario propuesto

- **Actor principal:** TOURIST
- **Actores secundarios:** AI AGENT
- **Objetivo:** Pedir ajustes sobre la propuesta ("quitá el trekking", "máximo $500") manteniendo contexto.
- **Precondiciones:** Existe un `AiItinerary` vigente en la conversación.
- **Flujo principal:** 1. Usuario envía instrucción (UC-T-13 reutilizado). 2. Sistema invoca UC-AI-05. 3. Sistema actualiza el `AiItinerary` (nueva versión) y lo devuelve.
- **Implementación (Oleada 6):** "nueva versión" es una **fila nueva** de `ai_itineraries` con `Version = anterior + 1`; la propuesta anterior queda intacta y sigue siendo recuperable por id (trazabilidad de la iteración). El ajuste es **parcial**: el backend decide de forma determinística qué ítems se preservan y cuáles se reemplazan, los preservados se reinsertan con su producto/día/slot/snapshot idénticos, y se descarta cualquier componente que el modelo proponga para un día preservado (salvo cuando la instrucción es "agregar").
- **Postcondiciones:** `AiItinerary` actualizado.
- **Entidades involucradas:** `AiConversation`, `AiMessage`, `AiItinerary`, `AiItineraryItem`
- **Endpoints probables:** `POST /api/ai/conversations/{id}/messages` (mismo endpoint que UC-T-13)

### UC-T-16 — Guardar itinerario IA

- **Actor principal:** TOURIST
- **Objetivo:** Persistir el itinerario actual como borrador reutilizable, sin comprarlo todavía.
- **Flujo principal:** 1. Usuario solicita guardar. 2. Sistema marca el `AiItinerary` como `SAVED` (en vez de transitorio a la sesión).
- **Postcondiciones:** Itinerario disponible en "Mis itinerarios".
- **Entidades involucradas:** `AiItinerary`
- **Endpoints probables:** `POST /api/ai/itineraries/{id}/save`

### UC-T-17 — Retomar itinerario IA guardado

- **Actor principal:** TOURIST
- **Objetivo:** Continuar planificando o revisar un itinerario guardado previamente.
- **Flujo principal:** 1. Usuario lista sus itinerarios guardados. 2. Usuario abre uno. 3. Sistema recupera el itinerario y su conversación asociada para permitir seguir iterando (UC-T-15) o reservar (UC-T-18).
- **Implementación (Oleada 6):** al abrirlo se revalida contra el catálogo (guardar no retiene cupos ni congela precio) y se informan los cambios: precio distinto, producto despublicado, slot cerrado o sin cupos. La respuesta trae `aiConversationId` para retomar la conversación por el endpoint de mensajes.
- **Entidades involucradas:** `AiItinerary`, `AiConversation`
- **Endpoints probables:** `GET /api/ai/itineraries/me`, `GET /api/ai/itineraries/{id}`

### UC-T-18 — Aceptar y reservar itinerario IA completo

- **Actor principal:** TOURIST
- **Actores secundarios:** Sistema
- **Objetivo:** Convertir el itinerario aceptado en una reserva real.
- **Precondiciones:** `AiItinerary` con al menos un `AiItineraryItem`.
- **Flujo principal:**
  1. Usuario confirma "reservar este itinerario".
  2. Sistema ejecuta UC-SYS-01 y UC-SYS-02 sobre **cada** componente del itinerario.
  3. Si todos los componentes siguen disponibles al precio esperado (o con cambios aceptados por el usuario), sistema ejecuta UC-SYS-05 (crear reserva padre + hijas).
  4. Sistema devuelve la reserva padre en `PENDING_PAYMENT`.
- **Flujos alternativos:** Si un componente cambió de precio/disponibilidad, el sistema presenta el detalle del cambio y pide confirmación explícita antes de continuar.
- **Excepciones:** Algún componente ya no tiene cupo y el usuario no ajusta → no se crea la reserva, se informa qué falló.
- **Postcondiciones:** Reserva padre + reservas hijas creadas en `PENDING_PAYMENT`.
- **Reglas de negocio relacionadas:** Decisión 2 y 4.
- **Entidades involucradas:** `AiItinerary`, `Reservation` (padre), `ReservationItem` (hijas), `Experience`, `Package`
- **Endpoints probables:** `POST /api/ai/itineraries/{id}/book`

### UC-T-19 — Pagar una reserva pendiente

- **Actor principal:** TOURIST
- **Objetivo:** Resolver el pago de una reserva en `PENDING_PAYMENT` para que se confirme.
- **Precondiciones:** Reserva en `PENDING_PAYMENT` y no expirada.
- **Flujo principal:** 1. Usuario inicia pago. 2. Sistema procesa el pago (simulado/placeholder en primeras oleadas). 3. Sistema invoca UC-SYS-07.
- **Excepciones:** Pago rechazado → reserva permanece `PENDING_PAYMENT` o pasa a `PAYMENT_FAILED`; cupo eventualmente liberado por expiración (UC-SYS-08).
- **Postcondiciones:** Reserva `CONFIRMED` (si el pago fue exitoso).
- **Reglas de negocio relacionadas:** Decisión 4 y 6.
- **Entidades involucradas:** `Reservation`
- **Endpoints probables:** `POST /api/reservations/{id}/pay`

---

## PROVIDER

### UC-P-01 — Registrar empresa y solicitar cuenta de Provider

- **Actor principal:** PROVIDER (usuario nuevo)
- **Actores secundarios:** ADMIN (revisa después)
- **Objetivo:** Crear la cuenta de usuario + la empresa asociada, quedando pendiente de aprobación.
- **Precondiciones:** Email no registrado.
- **Flujo principal:**
  1. Usuario envía datos personales + datos de la empresa (nombre, descripción, documento legal, contacto).
  2. Sistema crea `User` con rol `PROVIDER` y `Company` en estado `PENDING_APPROVAL`.
  3. Sistema notifica al ADMIN (o queda visible en su bandeja).
- **Excepciones:** Email ya registrado → 409.
- **Postcondiciones:** Usuario puede autenticarse, pero no puede publicar hasta aprobación (regla 1 del catálogo de reglas de FASE 1).
- **Entidades involucradas:** `User`, `Company`
- **Endpoints probables:** `POST /api/providers/register`

### UC-P-02 — Gestionar perfil de "Mi Empresa"

- **Actor principal:** PROVIDER
- **Objetivo:** Editar los datos públicos y de contacto de la empresa.
- **Precondiciones:** Empresa `APPROVED`.
- **Flujo principal:** 1. Provider edita datos. 2. Sistema valida propiedad (UC-SYS-03). 3. Sistema actualiza `Company`.
- **Entidades involucradas:** `Company`
- **Endpoints probables:** `GET /api/companies/me`, `PUT /api/companies/me`

### UC-P-03 — Agregar usuario Provider adicional a la empresa

- **Actor principal:** PROVIDER (dueño/admin de la empresa)
- **Objetivo:** Invitar a otro usuario para que administre la misma empresa.
- **Precondiciones:** Empresa `APPROVED`.
- **Flujo principal:** 1. Provider invita por email. 2. Sistema crea/vincula `User` con rol `PROVIDER` al mismo `company_id`.
- **Entidades involucradas:** `User`, `Company`
- **Endpoints probables:** `POST /api/companies/me/members`
- **Prioridad:** Backlog — diseño de dominio debe dejar espacio (relación N:1 `User`→`Company` desde el inicio), pero el endpoint no se implementa en las primeras oleadas.

### UC-P-04 — Crear experiencia

- **Actor principal:** PROVIDER
- **Objetivo:** Publicar una nueva experiencia en el catálogo de su empresa.
- **Precondiciones:** Empresa `APPROVED`.
- **Flujo principal:**
  1. Provider envía datos: título, destino, categorías, descripción, precio, duración, incluye/excluye, imágenes.
  2. Sistema valida propiedad (UC-SYS-03) y datos.
  3. Sistema crea `Experience` en estado `DRAFT`.
- **Excepciones:** Empresa no aprobada → 403.
- **Postcondiciones:** Experiencia creada, no visible públicamente hasta publicarse (UC-P-06).
- **Entidades involucradas:** `Experience`, `Company`, `Destination`, `Category`
- **Endpoints probables:** `POST /api/experiences`

### UC-P-05 — Editar experiencia

- **Actor principal:** PROVIDER
- **Objetivo:** Modificar los datos de una experiencia propia.
- **Precondiciones:** La experiencia pertenece a la empresa del Provider (UC-SYS-03).
- **Flujo principal:** 1. Provider edita campos. 2. Sistema valida y actualiza.
- **Excepciones:** No es dueño → 403.
- **Entidades involucradas:** `Experience`
- **Endpoints probables:** `PUT /api/experiences/{id}`

### UC-P-06 — Publicar/despublicar experiencia

- **Actor principal:** PROVIDER
- **Objetivo:** Controlar la visibilidad pública de una experiencia.
- **Precondiciones:** Experiencia con datos mínimos completos (precio, al menos una disponibilidad futura recomendable).
- **Flujo principal:** 1. Provider cambia estado a `PUBLISHED` o `UNPUBLISHED`. 2. Sistema valida propiedad y actualiza.
- **Entidades involucradas:** `Experience`
- **Endpoints probables:** `POST /api/experiences/{id}/publish`, `POST /api/experiences/{id}/unpublish`

### UC-P-07 — Crear paquete

- **Actor principal:** PROVIDER
- **Objetivo:** Publicar un paquete turístico de varios días compuesto por `Experience` propias y/o ítems descriptivos.
- **Precondiciones:** Empresa `APPROVED`.
- **Flujo principal:**
  1. Provider define datos generales (título, destino principal, precio, duración).
  2. Provider define estructura por día: cada `PackageItem` referencia una `Experience` propia existente **o** es un ítem descriptivo libre (texto, sin producto vendible independiente).
  3. Sistema valida que las `Experience` referenciadas pertenezcan a la misma empresa.
  4. Sistema crea `Package` en `DRAFT` con sus `PackageItem`.
- **Excepciones:** Referencia a una `Experience` de otra empresa → 403/400.
- **Reglas de negocio relacionadas:** Decisión 1.
- **Entidades involucradas:** `Package`, `PackageItem`, `Experience`, `Company`
- **Endpoints probables:** `POST /api/packages`

### UC-P-08 — Editar paquete

- **Actor principal:** PROVIDER
- **Objetivo:** Modificar datos generales o estructura por día de un paquete propio.
- **Precondiciones:** Propiedad validada (UC-SYS-03).
- **Flujo principal:** Análogo a UC-P-05 sobre `Package`/`PackageItem`.
- **Entidades involucradas:** `Package`, `PackageItem`
- **Endpoints probables:** `PUT /api/packages/{id}`

### UC-P-09 — Publicar/despublicar paquete

- **Actor principal:** PROVIDER
- **Objetivo:** Controlar visibilidad pública de un paquete.
- **Flujo principal:** Análogo a UC-P-06 sobre `Package`.
- **Entidades involucradas:** `Package`
- **Endpoints probables:** `POST /api/packages/{id}/publish`, `POST /api/packages/{id}/unpublish`

### UC-P-10 — Definir disponibilidad de una experiencia

- **Actor principal:** PROVIDER
- **Objetivo:** Establecer fechas/horarios concretos y cupos para que la experiencia sea reservable.
- **Precondiciones:** Experiencia propia existente.
- **Flujo principal:** 1. Provider define uno o varios slots (fecha, hora, cupos totales). 2. Sistema valida propiedad y crea/actualiza `ExperienceAvailability`.
- **Reglas de negocio relacionadas:** Decisión 8 (fechas/slots concretos, sin recurrencia por ahora).
- **Entidades involucradas:** `ExperienceAvailability`, `Experience`
- **Endpoints probables:** `POST /api/experiences/{id}/availability`, `GET /api/experiences/{id}/availability`

### UC-P-11 — Definir disponibilidad de un paquete

- **Actor principal:** PROVIDER
- **Objetivo:** Establecer fechas de salida concretas y cupos para un paquete.
- **Flujo principal:** Análogo a UC-P-10 sobre `PackageAvailability`.
- **Entidades involucradas:** `PackageAvailability`, `Package`
- **Endpoints probables:** `POST /api/packages/{id}/availability`

### UC-P-12 — Ver reservas recibidas

- **Actor principal:** PROVIDER
- **Objetivo:** Consultar las reservas (o partes de reservas, si vienen de un itinerario IA multi-proveedor) que corresponden a su empresa.
- **Flujo principal:** 1. Provider solicita listado. 2. Sistema filtra `ReservationItem` cuyo producto pertenece a la empresa del Provider.
- **Reglas de negocio relacionadas:** Regla 2 y 5 (aislamiento por empresa, visibilidad parcial en itinerarios IA).
- **Entidades involucradas:** `ReservationItem`, `Reservation`
- **Endpoints probables:** `GET /api/companies/me/reservations`

### UC-P-13 — Ver detalle de una reserva

- **Actor principal:** PROVIDER
- **Objetivo:** Ver el detalle de una reserva (turista, producto, fecha, personas) que le pertenece.
- **Precondiciones:** El `ReservationItem` corresponde a un producto de su empresa (UC-SYS-03).
- **Excepciones:** No pertenece a su empresa → 403.
- **Entidades involucradas:** `ReservationItem`, `Reservation`
- **Endpoints probables:** `GET /api/companies/me/reservations/{id}`

### UC-P-14 — Cancelar/rechazar una reserva (excepcional)

- **Actor principal:** PROVIDER
- **Objetivo:** Cancelar una reserva confirmada por causa de fuerza mayor (ej. error real de cupo).
- **Precondiciones:** `ReservationItem` de su empresa.
- **Flujo principal:** 1. Provider cancela con motivo. 2. Sistema actualiza estado y libera cupo (UC-SYS-08). 3. Se notifica al turista.
- **Reglas de negocio relacionadas:** Decisión 9 — caso excepcional, no flujo principal de negocio.
- **Entidades involucradas:** `ReservationItem`, `Reservation`
- **Endpoints probables:** `POST /api/companies/me/reservations/{id}/cancel`
- **Prioridad:** Oleada 8.

---

## ADMIN

### UC-A-01 — Revisar solicitudes de empresa pendientes

- **Actor principal:** ADMIN
- **Objetivo:** Ver el listado de empresas en estado `PENDING_APPROVAL`.
- **Flujo principal:** 1. Admin solicita listado. 2. Sistema devuelve empresas pendientes con sus datos de registro.
- **Entidades involucradas:** `Company`
- **Endpoints probables:** `GET /api/admin/companies?status=PENDING_APPROVAL`

### UC-A-02 — Aprobar solicitud de empresa

- **Actor principal:** ADMIN
- **Objetivo:** Habilitar a una empresa para operar (crear/publicar productos).
- **Precondiciones:** Empresa en `PENDING_APPROVAL`.
- **Flujo principal:** 1. Admin aprueba. 2. Sistema cambia estado a `APPROVED`. 3. Se notifica al Provider.
- **Postcondiciones:** El Provider puede crear/publicar experiencias y paquetes.
- **Reglas de negocio relacionadas:** Regla 1.
- **Entidades involucradas:** `Company`
- **Endpoints probables:** `POST /api/admin/companies/{id}/approve`

### UC-A-03 — Rechazar solicitud de empresa

- **Actor principal:** ADMIN
- **Objetivo:** Rechazar una solicitud que no cumple criterios.
- **Flujo principal:** 1. Admin rechaza con motivo. 2. Sistema cambia estado a `REJECTED`. 3. Se notifica al Provider.
- **Entidades involucradas:** `Company`
- **Endpoints probables:** `POST /api/admin/companies/{id}/reject`

### UC-A-04 — Gestionar destinos

- **Actor principal:** ADMIN
- **Objetivo:** Mantener el catálogo maestro de países, regiones y ciudades.
- **Flujo principal:** CRUD sobre `Destination` respetando la jerarquía Country → Region → City.
- **Reglas de negocio relacionadas:** Decisión 10.
- **Entidades involucradas:** `Destination`
- **Endpoints probables:** `POST/PUT/DELETE /api/admin/destinations`

### UC-A-05 — Gestionar categorías

- **Actor principal:** ADMIN
- **Objetivo:** Mantener el catálogo maestro de categorías (naturaleza, gastronomía, cultura, etc.).
- **Flujo principal:** CRUD sobre `Category`.
- **Entidades involucradas:** `Category`
- **Endpoints probables:** `POST/PUT/DELETE /api/admin/categories`

### UC-A-06 — Gestionar usuarios

- **Actor principal:** ADMIN
- **Objetivo:** Ver y, si corresponde, suspender cuentas de usuario.
- **Flujo principal:** 1. Admin lista/busca usuarios. 2. Admin suspende una cuenta si corresponde.
- **Entidades involucradas:** `User`
- **Endpoints probables:** `GET /api/admin/users`, `POST /api/admin/users/{id}/suspend`
- **Prioridad:** Oleada 8.

### UC-A-07 — Suspender/despublicar contenido

- **Actor principal:** ADMIN
- **Objetivo:** Retirar de circulación una experiencia o paquete por incumplir políticas.
- **Flujo principal:** 1. Admin revisa contenido reportado o auditado. 2. Admin despublica forzosamente.
- **Entidades involucradas:** `Experience`, `Package`
- **Endpoints probables:** `POST /api/admin/experiences/{id}/suspend`, `POST /api/admin/packages/{id}/suspend`
- **Prioridad:** Oleada 8.

### UC-A-08 — Suspender una empresa

- **Actor principal:** ADMIN
- **Objetivo:** Bloquear temporalmente a una empresa incumplidora (oculta todo su catálogo).
- **Flujo principal:** 1. Admin suspende. 2. Sistema cambia `Company.status` a `SUSPENDED` y oculta sus productos del catálogo público.
- **Entidades involucradas:** `Company`
- **Endpoints probables:** `POST /api/admin/companies/{id}/suspend`
- **Prioridad:** Oleada 8.

---

## AI AGENT

> Estos casos de uso se orquestan desde UC-T-13/UC-T-15 y consumen los casos de uso internos del sistema (UC-SYS) para garantizar que nunca se inventa información. Ninguno expone un endpoint propio, **con una excepción implementada en Oleada 6**: UC-AI-06 sí tiene un endpoint de consulta para pedir la justificación de un componente concreto del itinerario (ver su ficha).

### UC-AI-01 — Interpretar preferencias de viaje

- **Actor principal:** AI AGENT
- **Objetivo:** Convertir el mensaje en lenguaje natural del turista en `TripPreferences` estructuradas (destino, fechas, nº viajeros, presupuesto, intereses, restricciones, duración).
- **Precondiciones:** Mensaje recibido (UC-T-13).
- **Flujo principal:** 1. El agente extrae entidades del texto. 2. Combina con `TripPreferences` previas de la misma conversación (si existen) para actualizar, no reemplazar desde cero.
- **Excepciones:** Información insuficiente (ej. no menciona destino) → el agente responde pidiendo la información faltante, sin ejecutar retrieval todavía.
- **Postcondiciones:** `TripPreferences` actualizadas.
- **Entidades involucradas:** `AiMessage`, `TripPreferences`

### UC-AI-02 — Recuperar oferta relevante del catálogo

- **Actor principal:** AI AGENT
- **Objetivo:** Consultar (RAG/retrieval) las `Experience` y `Package` reales, `PUBLISHED` y con disponibilidad futura, que sean candidatas según `TripPreferences`.
- **Precondiciones:** `TripPreferences` con al menos destino definido.
- **Flujo principal:** 1. Sistema arma la consulta de recuperación (filtros duros: destino, fechas, presupuesto; señales blandas: intereses). 2. Devuelve conjunto candidato de productos reales con su precio y disponibilidad vigente.
- **Reglas de negocio relacionadas:** "La IA no inventa productos": este caso de uso es el único punto de acceso de la IA al catálogo.
- **Entidades involucradas:** `Experience`, `Package`, `ExperienceAvailability`, `PackageAvailability`

### UC-AI-03 — Priorizar Provider Package que encaje

- **Actor principal:** AI AGENT
- **Objetivo:** Si un `Package` existente satisface suficientemente bien las preferencias (destino, duración, presupuesto, intereses), recomendarlo directamente en vez de componer uno nuevo.
- **Precondiciones:** UC-AI-02 devolvió al menos un `Package` candidato.
- **Flujo principal:** 1. El agente evalúa el/los `Package` candidatos contra `TripPreferences`. 2. Si el ajuste supera el umbral definido, lo selecciona como base del `AiItinerary`.
- **Postcondiciones:** `AiItinerary` creado a partir de un único `Package` (con posibilidad de seguir combinando, ver UC-AI-04).
- **Entidades involucradas:** `Package`, `AiItinerary`, `AiItineraryItem`

### UC-AI-04 — Componer itinerario combinando Experiences/Packages

- **Actor principal:** AI AGENT
- **Objetivo:** Cuando ningún `Package` encaja suficientemente solo, construir un itinerario día a día combinando `Experience` (y opcionalmente `Package` completos) posiblemente de distintos proveedores.
- **Precondiciones:** UC-AI-02 ejecutado.
- **Flujo principal:** 1. El agente distribuye componentes candidatos por día respetando presupuesto, intereses y duración. 2. Calcula precio estimado total sumando precios reales. 3. Crea/actualiza `AiItinerary` con sus `AiItineraryItem` (cada uno con referencia a `Experience` o `Package`, día y orden).
- **Reglas de negocio relacionadas:** Decisión 3 (puede combinar ambos tipos); "la IA no inventa" (todo ítem referencia un producto real existente).
- **Entidades involucradas:** `AiItinerary`, `AiItineraryItem`, `Experience`, `Package`

### UC-AI-05 — Ajustar itinerario ante nueva instrucción

- **Actor principal:** AI AGENT
- **Objetivo:** Modificar el `AiItinerary` vigente según una instrucción de seguimiento, conservando el contexto conversacional.
- **Precondiciones:** Existe `AiItinerary` vigente en la conversación (UC-T-15).
- **Flujo principal:** 1. Reinterpreta preferencias con la nueva instrucción (reutiliza UC-AI-01). 2. Vuelve a ejecutar UC-AI-02/03/04 con las restricciones actualizadas, preservando lo que el usuario no pidió cambiar cuando sea razonable.
- **Postcondiciones:** Nueva versión de `AiItinerary`.
- **Entidades involucradas:** `AiItinerary`, `AiItineraryItem`, `TripPreferences`

### UC-AI-06 — Explicar/justificar la propuesta

- **Actor principal:** AI AGENT
- **Objetivo:** Generar el texto explicativo que acompaña al itinerario (por qué se eligió cada componente), basado únicamente en datos reales recuperados.
- **Flujo principal:** 1. El agente redacta la explicación usando los datos de `AiItinerary`/`AiItineraryItem` ya calculados, sin agregar precios o datos no presentes en ellos.
- **Implementación (Oleada 6):** el backend calcula primero una lista de *hechos* desde Postgres (destino, categorías compartidas con las preferencias, fecha del slot dentro del rango del viaje, cupos disponibles, precio del snapshot, encaje con el presupuesto solo si la moneda coincide) y el modelo **únicamente los redacta**. Los hechos se devuelven junto al texto para que la explicación sea auditable: nada que no esté en esa lista puede aparecer en la respuesta.
- **Entidades involucradas:** `AiItinerary`, `AiItineraryItem`
- **Endpoint:** `GET /api/ai/itineraries/{id}/items/{itemId}/explanation` — solo lectura, no altera precio, disponibilidad ni estado del itinerario.

---

## CASOS DE USO INTERNOS DEL SISTEMA (UC-SYS)

### UC-SYS-01 — Verificar disponibilidad y cupos

- **Objetivo:** Confirmar que un producto (`Experience` o `Package`) tiene cupo suficiente en la fecha/slot solicitado, en el momento exacto de la operación.
- **Invocado por:** UC-T-08, UC-T-09, UC-T-18.
- **Entidades involucradas:** `ExperienceAvailability`, `PackageAvailability`

### UC-SYS-02 — Recalcular/revalidar precio

- **Objetivo:** Recuperar el precio vigente real de un producto (puede diferir del mostrado previamente) antes de confirmar cualquier reserva.
- **Invocado por:** UC-T-08, UC-T-09, UC-T-18, UC-AI-04.
- **Entidades involucradas:** `Experience`, `Package`

### UC-SYS-03 — Validar propiedad de una empresa

- **Objetivo:** Garantizar que un `PROVIDER` solo pueda leer/modificar recursos (`Experience`, `Package`, `ReservationItem`) que pertenecen a su propio `company_id` (tomado del JWT).
- **Invocado por:** Todos los UC-P-xx que editan o leen datos de una empresa específica.
- **Entidades involucradas:** `Company`, `User`

### UC-SYS-04 — Crear reserva simple

- **Objetivo:** Crear una `Reservation` de un solo componente (una `Experience` o un `Package`) en `PENDING_PAYMENT`.
- **Invocado por:** UC-T-08, UC-T-09.
- **Entidades involucradas:** `Reservation`, `ReservationItem`

### UC-SYS-05 — Crear reservas múltiples desde un itinerario IA

- **Objetivo:** A partir de un `AiItinerary` aceptado, crear una `Reservation` padre y un `ReservationItem` hijo por cada `AiItineraryItem`, preservando a qué empresa pertenece cada uno.
- **Invocado por:** UC-T-18.
- **Reglas de negocio relacionadas:** Decisión 2.
- **Entidades involucradas:** `AiItinerary`, `AiItineraryItem`, `Reservation`, `ReservationItem`

### UC-SYS-06 — Retener/descontar cupo de forma atómica

- **Objetivo:** Evitar overbooking cuando varias reservas compiten por el mismo cupo al mismo tiempo (control de concurrencia).
- **Invocado por:** UC-SYS-04, UC-SYS-05.
- **Entidades involucradas:** `ExperienceAvailability`, `PackageAvailability`

### UC-SYS-07 — Confirmar reserva tras pago exitoso

- **Objetivo:** Pasar una `Reservation` (y sus `ReservationItem`) de `PENDING_PAYMENT` a `CONFIRMED` de forma automática cuando el pago se resuelve exitosamente y hay cupo confirmado.
- **Invocado por:** UC-T-19.
- **Reglas de negocio relacionadas:** Decisión 6.
- **Entidades involucradas:** `Reservation`, `ReservationItem`

### UC-SYS-08 — Liberar cupo por expiración o cancelación

- **Objetivo:** Si una reserva `PENDING_PAYMENT` expira sin pago, o se cancela, liberar el cupo retenido para que vuelva a estar disponible.
- **Invocado por:** UC-T-11, UC-P-14, proceso propio de expiración por tiempo.
- **Entidades involucradas:** `Reservation`, `ExperienceAvailability`, `PackageAvailability`
- **Prioridad:** Oleada 8.

### UC-SYS-09 — Reindexar catálogo para recuperación (RAG)

- **Objetivo:** Mantener actualizado el índice/mecanismo de recuperación que usa la IA (UC-AI-02) cada vez que una `Experience` o `Package` se publica, edita o despublica.
- **Invocado por:** UC-P-06, UC-P-09, UC-P-05, UC-P-08 (como efecto secundario).
- **Entidades involucradas:** `Experience`, `Package`
- **Prioridad:** Oleada 7 (se implementa junto con la base de IA, no antes).

---

## Backlog / fuera de alcance inicial (mencionado por completitud, no se detalla con template completo)

- Reseñas/calificaciones de experiencias y paquetes.
- Favoritos del turista.
- Integración real de pasarela de pago (hoy solo existe el estado `PENDING_PAYMENT`/`CONFIRMED` como punto de extensión).
- Políticas de reembolso parcial/total.
- Notificaciones push/email.
- UC-P-03 (multi-usuario por empresa) — el dominio se diseña para soportarlo, pero no se implementa endpoint en las primeras oleadas.

---

## Oleadas de implementación (orden de prioridad y dependencia)

| Oleada | Enfoque | Casos de uso |
|---|---|---|
| 0 | Fundamentos de cuenta y autorización | UC-AUTH-01..04, UC-SYS-03 |
| 1 | Catálogo maestro + alta de proveedor | UC-A-04, UC-A-05, UC-P-01, UC-A-01, UC-A-02, UC-A-03, UC-P-02 |
| 2 | Experiencias, disponibilidad y reserva directa | UC-P-04, UC-P-05, UC-P-06, UC-P-10, UC-T-03, UC-T-04, UC-T-05, UC-T-08, UC-SYS-01, UC-SYS-04, UC-SYS-06, UC-T-10, UC-P-12, UC-P-13 |
| 3 | Pago (desacoplado) y confirmación | UC-T-19, UC-SYS-07, UC-SYS-02 |
| 4 | Paquetes de proveedor | UC-P-07, UC-P-08, UC-P-09, UC-P-11, UC-T-06, UC-T-07, UC-T-09 |
| 5 | IA — fundamentos (recomendación) | UC-T-12, UC-T-13, UC-AI-01, UC-AI-02, UC-AI-03, UC-AI-04, UC-T-14 |
| 6 | IA — iteración y persistencia | UC-T-15, UC-AI-05, UC-AI-06, UC-T-16, UC-T-17 |
| 7 | IA — de propuesta a reserva | UC-T-18, UC-SYS-05, UC-SYS-09 |
| 8 | Moderación, cancelaciones, gestión de usuarios | UC-A-06, UC-A-07, UC-A-08, UC-T-11, UC-P-14, UC-SYS-08 |
| Backlog | Multi-usuario por empresa, reseñas, favoritos, pagos reales | UC-P-03 y otros |

Cada oleada solo introduce las entidades estrictamente necesarias para los casos de uso que la componen (consistente con "no construir entidades sin un caso de uso que las necesite").
