# TurisClick — contrato de diseño

Una identidad, dos densidades. Este documento es la autoridad visual del proyecto: si una pantalla y este archivo no coinciden, el que está mal es la pantalla.

Estado: **vertical slice** (2026-10-04). Lo aplican hoy el shell y el dashboard del Backoffice, su listado de Experiencias, y en Tourist Mobile el Home, la ficha de experiencia y la entrada del asistente. El resto de las pantallas sigue con el sistema anterior **a propósito**, hasta que se apruebe la dirección.

---

## 1. Filosofía visual

**El producto atómico de TurisClick no es una experiencia: es una salida con cupo.** Fecha real, lugares reales, operador aprobado. Eso es lo que lo distingue de cualquier marketplace, y es lo que la interfaz tiene que hacer visible antes que cualquier otra cosa.

Tres reglas que resuelven la mayoría de las decisiones:

1. **La honestidad del dominio es material de diseño, no una limitación.** No hay "populares" sin métrica, no se suman monedas distintas, un precio ausente dice "Consultar precio" y nunca 0. Si un dato no existe, la interfaz lo dice; no lo decora ni lo rellena.
2. **Bolivia entra por la fotografía y el contenido.** Nombres de lugar como tipografía protagonista sobre fotos reales, crédito de autor visible, paleta mineral (agua de altiplano, piedra, luz de altura). Sin aguayos, sin patrones textiles, sin colores de bandera.
3. **Una sola cosa memorable por pantalla.** El resto es silencio disciplinado. Cero gradientes decorativos, cero glassmorphism, cero sombras exageradas, cero cards dentro de cards dentro de cards.

**Gesto propio de cada superficie** — y nada más:

| Superficie | Gesto | Por qué es nuestro |
|---|---|---|
| Mobile | **Tira de salidas**: `sáb 11 oct · 4 lugares` bajo la foto | Es el dato que decide la compra; ningún e-commerce lo necesita |
| Backoffice | **Medidor de cupo**: barra baja + `18/20` tabular | El trabajo de un operador turístico es ocupación |

**Densidades distintas, misma marca.** Backoffice: SaaS denso, escaneable, acciones a mano, radios chicos. Mobile: fotografía a sangre, aire, tipografía grande, radios generosos. Los tokens son los mismos; lo que cambia es la escala a la que se usan.

**Prohibido** (son los tells que esta fase vino a borrar): códigos internos en copy visible; emoji como control o ícono; eyebrows en VERSALITAS ESPACIADAS arriba de cada título; strings armados con ` · ` como recurso de jerarquía; `→` pegado al texto de un botón; cream + serif de alto contraste + terracota.

---

## 2. Color

Una marca para las dos apps. Los valores se declaran una vez por app (`src/index.css` en Backoffice, `tailwind.config.js` + `src/theme/colors.ts` en Mobile) y **nunca se escriben como literal en una pantalla**.

### Marca

| Token | Hex | Uso |
|---|---|---|
| `brand-900` | `#06303C` | Sidebar, scrims sobre foto, superficies de marca oscuras |
| `brand-700` | `#005F73` | **Primary**: botones, enlaces, ring de foco, estado activo |
| `brand-500` | `#0E7A8E` | Hover de primary, rellenos, barra del medidor de cupo |

### Acento — un solo matiz, dos papeles con pares legales

| Token | Hex | Único uso legal |
|---|---|---|
| `accent-500` | `#E08A00` | Relleno de énfasis **con texto `ink`** (5.44:1) o sobre `brand-900` (5.22:1) |
| `accent-700` | `#A85A08` | Acento como **texto** sobre claro (5.08:1), y como indicador no textual sobre claro |

`accent-500` no llega a 3:1 sobre blanco (2.69): **nunca comunica estado por sí solo sobre fondo claro**. Un punto, una barra o un borde de acento sobre superficie clara usan `accent-700`.

El acento marca **una sola cosa por pantalla**: la que mueve dinero o la que es escasa. Si hay dos acentos compitiendo, uno está mal.

### Neutrales y bordes

| Token | Hex | Uso |
|---|---|---|
| `ink` | `#102A43` | Texto principal |
| `ink-muted` | `#5B7285` | Texto secundario (pasa AA sobre las tres superficies: 4.57–5.01) |
| `surface` | `#FFFFFF` | Tarjetas, inputs, barras |
| `background` | `#F6F9FA` mobile / `#F8FAFC` web | Fondo de página |
| `border-subtle` | `#E2E8F0` | **Solo divisores decorativos.** No delimita controles |
| `border-control` | `#828E9C` | Inputs, chips, selects, celdas de calendario — ≥3:1 en toda superficie |

`border-subtle` mide 1.2:1: es legal como divisor y **ilegal** como borde de un control, porque un campo cuyo borde no se ve es un campo que no se ve. Los `#8A96A3` del plan original fallaban sobre los fondos de página (2.85); `#828E9C` pasa en las cuatro superficies.

### Estados semánticos

Patrón **texto `-800` sobre fondo `-100`**, que es el que mide mejor (6.37–6.80) y ya existía en Mobile:

| Estado | Texto | Fondo | Ratio |
|---|---|---|---|
| success | `#166534` | `#DCFCE7` | 6.49 |
| warning | `#92400E` | `#FEF3C7` | 6.37 |
| danger | `#991B1B` | `#FEE2E2` | 6.80 |
| info | `#1E40AF` | `#DBEAFE` | 7.15 |

Rellenos sólidos para botones de acción (con texto blanco): `success #15803D` (5.02), `danger #B91C1C` (6.47).

**Vocabulario único de estados** compartido por catálogo, asistente y backoffice: `disponible` · `últimos lugares` · `sin cupo` · `fecha pasada` · `precio cambió` · `publicada` · `borrador` · `pausada`. Cada uno tiene **un** color y **siempre** va con texto: el color nunca viaja solo.

### Retirados

`#123A5C`, `#0F766E`, `#0A9396` (como texto: 3.73:1), `#C2410C`, `#EE9B00`, y los 16 hex inventados de Mobile. En las pantallas del slice no queda ninguno.

---

## 3. Tipografía

| Rol | Familia | Dónde |
|---|---|---|
| UI, body, datos | **Inter** | Las dos apps, todo |
| Display | **Newsreader** (serif variable) | **Experimental**, solo Mobile, solo sobre fotografía: nombre de destino en el hero y título de ficha |

Newsreader está en evaluación: no entra en botones, formularios, body ni backoffice, y cae a la fuente del sistema si no carga. La app no se vuelve editorial; se vuelve editorial **la foto**.

Escala (6 pasos, declarada como token, sin tamaños ad hoc):

| Paso | px | Peso | Uso |
|---|---|---|---|
| `display` | 30/34 | 600 | Hero de Mobile (Newsreader) |
| `title` | 22 | 700 | Título de pantalla, título de ficha |
| `heading` | 17 | 600 | Encabezado de sección, título de tarjeta |
| `body` | 15 | 400 | Texto corrido |
| `label` | 13 | 500 | Etiquetas, metadatos, celdas de tabla |
| `caption` | 12 | 500 | Badges, crédito de foto, pies |

**Nada por debajo de 12px.** Los números —precios, cupos, fechas, contadores— llevan `tabular-nums`: una columna de cifras no debe bailar de ancho.

---

## 4. Spacing

Base **4px estricta**, 6 pasos: `4 · 8 · 12 · 16 · 24 · 32`. Fuera de la escala no hay nada (el `1.5` de 6px queda retirado).

Ritmo: `16` es el canalón de pantalla en Mobile y el padding interno de tarjeta en las dos apps; `24` separa bloques dentro de una pantalla; `32` separa secciones.

---

## 5. Radius

Una escala, dos rangos de uso.

| Token | px | Backoffice | Mobile |
|---|---|---|---|
| `sm` | 8 | Inputs, botones, badges | Chips |
| `md` | 12 | Tarjetas, diálogos | Botones, inputs |
| `lg` | 16 | — | Tarjetas, hojas |
| `full` | — | Avatares, badges de estado | Avatares, chips |

---

## 6. Elevation

Tres niveles, y **un solo primitivo que emite sombra iOS y `elevation` Android a la vez**: declarar solo `elevation` deja iPhone completamente plano, que es lo que pasaba en 28 lugares.

| Nivel | Uso | Web | Nativo |
|---|---|---|---|
| `flat` | Superficies sobre fondo | borde `border-subtle` | borde, sin sombra |
| `raised` | Tarjetas, barras pegadas | `shadow-sm` | `shadowOpacity .08 / radius 8 / offset 0,2` + `elevation 2` |
| `overlay` | Diálogos, hojas, menús | `shadow-lg` | `shadowOpacity .16 / radius 20 / offset 0,8` + `elevation 12` |

Sombra sutil y fría (`shadowColor: ink`). Nunca dos niveles anidados: una tarjeta `raised` no contiene otra `raised`.

---

## 7. Iconografía

**Lucide en las dos apps**: `lucide-react` en web, `lucide-react-native` en Mobile. Ícono-por-ícono idéntico entre superficies.

Tamaños: 16 (inline en texto), 20 (botones y navegación), 24 (tab bar, acciones destacadas). Trazo 2px (el default de Lucide), nunca relleno.

- Todo botón solo-ícono lleva nombre accesible y el ícono va `aria-hidden` / `accessibilityElementsHidden`.
- **El emoji sobrevive solo donde es contenido** — intereses y categorías del onboarding. Como control (navegación, volver, cerrar, enviar, calendario, confirmación) está prohibido.
- Los glifos tipográficos (`←` `✕` `➤` `‹` `›` `−`) no son íconos: tienen métricas de texto y por eso había un tamaño parchado a mano en cada pantalla.

---

## 8. Imagery

La fotografía es el contenido, no el fondo.

- **Proporciones fijas**: `16:10` en tarjetas de catálogo, `4:3` en hero de ficha, `1:1` en tarjeta de destino. Sin alto libre: el layout no salta al cargar.
- **Scrim** sobre foto cuando lleva texto: degradado vertical de `brand-900` al 0 → 70% desde abajo. Medido: blanco sobre scrim al 70% encima de una foto media = 9.34:1.
- **Carga progresiva**: placeholder → imagen con crossfade. Nunca un hueco blanco del alto de la tarjeta.
- **Fallback digno, nunca un emoji**: superficie mineral (degradado `brand-900`→`brand-700`) con el nombre del lugar en display. Un producto sin foto se ve intencional, no roto.
- **Criterio de curaduría**: una foto que no muestra el lugar *como destino* no entra. Un hall de aeropuerto, un cementerio, una polilla de museo o una foto satelital no son fotos de viaje, aunque la URL exista. Ver `docs/image-curation-debt.md`.
- **Crédito de autor** en `caption` al pie de la foto en la ficha: es trazabilidad y es señal de honestidad.

---

## 9. Botones

| Variante | Relleno | Texto | Cuándo |
|---|---|---|---|
| `primary` | `brand-700` | blanco | La acción de la pantalla. Una sola |
| `accent` | `accent-500` | `ink` | Solo donde el énfasis es dinero o escasez |
| `outline` | `surface` + `border-control` | `brand-700` | Acción secundaria |
| `ghost` | — | `ink-muted` | Terciaria, dentro de filas y barras |
| `destructive` | `#B91C1C` | blanco | Destruye o revierte algo público. Siempre confirma antes |

Alturas: Mobile **52** (CTA) y **44** (secundarios, mínimo táctil absoluto); Backoffice **40** en escritorio y **44** por debajo de `sm`. Jamás un control interactivo por debajo de 44px en pantalla táctil; si la forma no da, `hitSlop`.

Un botón que dispara red tiene estado `loading` con el verbo en gerundio, y queda deshabilitado mientras corre. Si está deshabilitado por una razón, **la razón se escribe** ("Sin fechas disponibles"), no se deja adivinar.

---

## 10. Inputs

`surface` + `border-control` + radio `sm`/`md`, alto 44 en Mobile y 40 en web.

- **Label siempre asociada** al control (`htmlFor`/`id` en web, `accessibilityLabel` en Mobile). Una etiqueta que no enfoca su campo es un bug.
- El error va **debajo**, en `danger`, con texto: el borde rojo nunca viaja solo.
- Foco visible en web: `outline: 2px brand-700` con `offset: 2px`, definido en un solo lugar.

---

## 11. Cards

Una tarjeta = una cosa que se puede tocar. Anatomía fija: media (ratio del sistema) → contenido (`16` de padding) → pie de datos. `raised`, radio `lg` en Mobile y `md` en web, **sin tarjeta anidada**.

En Mobile, la tarjeta de catálogo cierra con la **tira de salidas** cuando hay disponibilidad real, y con nada cuando no la hay. En Backoffice, las "tarjetas" son contenedores de tabla o de bloque: no se decoran, se usan para agrupar.

---

## 12. Badges y estados

Pastilla radio `full`, `caption` en 600, par de color semántico de §2, **siempre con texto**. Jamás un punto de color solo.

El medidor de cupo es el estado más repetido del Backoffice: barra de 4px (`brand-500` sobre `border-subtle`, 4.06:1) + `18/20` en `label` tabular + etiqueta textual cuando el estado importa (`Completo`, `Últimos lugares`). Número y barra juntos: la barra sola no se lee, el número solo no se escanea.

---

## 13. Motion

Mínima y con causa. Nunca decorativa, nunca de entrada por sección, nunca hover en cada tarjeta.

| Momento | Movimiento | Duración |
|---|---|---|
| Presión de tarjeta o botón | `scale .98` | 120ms, `ease-out` |
| Hover/foco en web | color | 150ms |
| Skeleton → contenido | crossfade | 180ms |
| Imagen cargada | crossfade | 200ms |
| Diálogo/hoja | entra con 8px de desplazamiento | 200ms |

`prefers-reduced-motion` (web) y `AccessibilityInfo.isReduceMotionEnabled` (Mobile) desactivan transformaciones y dejan solo crossfades; incluye el spinner.

---

## 14. Accesibilidad — piso no negociable

1. Texto normal ≥ **4.5:1**, texto grande y controles ≥ **3:1**. Los pares de este documento están medidos, no estimados.
2. Táctil ≥ **44px** en Mobile; en web ≥44 por debajo de `sm`.
3. Toda entrada con nombre accesible; todo botón solo-ícono con nombre.
4. Foco visible siempre en web; nunca `outline: none` sin reemplazo.
5. El estado **nunca** se comunica solo por color: color + texto, o color + forma.
6. El movimiento se puede apagar.
7. Un cambio que arregla contraste no se revierte por estética.

---

## 15. Responsive

**Mobile** (375–430 ancho de referencia): canalón 16; una columna; CTA primario al alcance del pulgar, abajo; los controles de salida pueden quedar arriba pero duplicados con gesto nativo de volver.

**Backoffice**: `< 768` sidebar en drawer, contenido a una columna, tablas con scroll horizontal real (ancho mínimo por columna, no columnas estrujadas) o listas de tarjetas; `768–1279` sidebar fija de 256, contenido fluido; `≥ 1280` contenido con tope de 1152 y el sidebar fuera del flujo.

Una tabla que se comprime hasta ser ilegible es peor que una tabla con scroll: las celdas llevan ancho mínimo y no envuelven.
