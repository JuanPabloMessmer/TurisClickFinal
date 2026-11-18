# Auditoría de validaciones de datos — TurisClick

> **Estado:** versión 3 — **plan definitivo de la fase 1, pendiente de tu autorización para implementar.**
> Incorpora tus criterios sobre D-1…D-11 y tus respuestas a C-1…C-10 (ambos del 17/11/2026).
> No se implementó nada, no se ejecutaron migraciones ni consultas, no se modificaron datos y no se tocó Azure,
> Terraform, GitHub Actions ni producción.
>
> **Relevamiento del código:** 17/11/2026, sobre `master` (`c7ba74f`).
>
> **Origen:** observaciones del tutor sobre el backoffice: se pueden registrar empresas con nombres excesivamente
> largos, teléfonos con letras, NIT inválidos, correos exageradamente largos y nombres de categoría incorrectos, y
> los errores del backend aparecen en inglés.

## Cómo leer el origen de cada regla

| Etiqueta | Significado |
|---|---|
| **[N]** | **Norma oficial**: SIN, Ministerio de Obras Públicas / ATT, UIT, ISO. |
| **[T]** | **Restricción técnica**: un estándar técnico (RFC) o un límite que ya existe en la base o en el código. |
| **[D]** | **Decisión de diseño** de TurisClick: aprobada por vos, pero no la impone ninguna norma. |

Verificación: **✔** leído en la fuente durante esta auditoría · **⚠** no verificado (fuente ilegible o sólo
fuentes secundarias).

## Índice

1. [Resumen ejecutivo](#1-resumen-ejecutivo)
2. [Decisiones aprobadas](#2-decisiones-aprobadas)
3. [Fundamento: NIT](#3-fundamento-nit)
4. [Fundamento: teléfonos](#4-fundamento-teléfonos)
5. [Fundamento: correos y precios](#5-fundamento-correos-y-precios)
6. [Normalización y claves de comparación](#6-normalización-y-claves-de-comparación)
7. [Matriz definitiva — fase 1](#7-matriz-definitiva--fase-1)
8. [Matriz de las fases 2 y 3](#8-matriz-de-las-fases-2-y-3)
9. [Errores: situación actual y diseño](#9-errores-situación-actual-y-diseño)
10. [Plan definitivo de la fase 1](#10-plan-definitivo-de-la-fase-1)
11. [Pruebas de la fase 1](#11-pruebas-de-la-fase-1)
12. [PostgreSQL: impacto e incompatibilidades](#12-postgresql-impacto-e-incompatibilidades)
13. [Fase 0: diagnóstico en staging (sólo lectura)](#13-fase-0-diagnóstico-en-staging-sólo-lectura)
14. [Corrección segura de datos existentes](#14-corrección-segura-de-datos-existentes)
15. [Riesgos](#15-riesgos)
16. [Lo que necesito de vos antes de empezar](#16-lo-que-necesito-de-vos-antes-de-empezar)
17. [Anexos: relevamiento](#17-anexos-relevamiento)

---

## 1. Resumen ejecutivo

Lo que encontró el tutor tiene una causa común: **las reglas viven repetidas y desalineadas** en formulario, DTO y
base, ninguna capa valida **formato** de NIT, teléfono ni nombres, y los errores de validación salen con los textos
por defecto de ASP.NET y de zod, en inglés.

| Hallazgo del tutor | Causa | Qué lo corrige en la fase 1 |
|---|---|---|
| Nombres de empresa excesivamente largos | 150 caracteres de cualquier tipo en las tres capas | Regla R-01: 3–100, ≥2 letras, caracteres de razón social |
| Teléfonos con letras | Sólo `MaxLength(30)`; el formulario ni muestra el error | Regla R-05: fijos, celulares y 800 de Bolivia según el PTFN, o internacional E.164 |
| NIT inválidos | 3–50 caracteres cualesquiera | Regla R-03: 5–12 dígitos |
| Correos exageradamente largos | `[EmailAddress]` sólo exige una `@`; límites distintos (150/255) | Regla R-04: formato real ASCII, ≤254 con límites por parte |
| Nombres de categoría incorrectos | Sin esquema; unicidad sensible a mayúsculas | Reglas R-07 y R-08 |
| Errores en inglés | Mensajes por defecto de DataAnnotations y zod; banner que concatena todo | Manejo centralizado (§9.2), mensajes bajo cada campo |

Además corrige: borrado silencioso de categorías en uso (FK en cascada), errores 500 por unicidad o restricciones
de la base, y validación hecha antes de normalizar.

---

## 2. Decisiones aprobadas

| # | Decisión final | Origen | Fase |
|---|---|---|---|
| **D-1 / C-1 / C-7** NIT | **5 a 12 dígitos numéricos**, sin separadores. El **máximo de 12 está respaldado por el SIN** [N ✔]; el **mínimo de 5 es una decisión de diseño**, no una exigencia legal verificada [D]. Sólo NIT boliviano; no se admiten operadores sin NIT en esta etapa [D]. | N + D | 1 |
| **D-2 / C-2** Correos | Sólo **ASCII**, **≤254** caracteres con los límites de cada parte [T ✔]; formato real; recorte de extremos; rechazo de espacios internos; únicos entre cuentas. | T + D | 1 (cuentas y empresa), 3 (app) |
| **D-3** Nombre de empresa | 3–100, ≥2 letras, números y símbolos habituales de razón social; rechazo de basura sólo con reglas objetivas. | D | 1 |
| **D-4 / C-3** Duplicados | **Bloqueo** de duplicados exactos normalizados (mayúsculas, tildes, espacios). En empresas, **advertencia** (no bloqueo) para nombres que sólo difieren en puntuación o forma societaria (`Andes Tours S.R.L.` / `Andes Tours SRL`). Backend + índice único + concurrencia. | D + T | 1 (empresas, categorías), 2 (destinos) |
| **D-5 / C-4 / C-5** Teléfonos | Fijos, celulares (5, 6 o 7, sin limitarse a prefijos en uso) y líneas 800 de Bolivia [N ✔], con o sin `+591`; **también extranjeros** en formato internacional válido [N ✔ E.164]. Sin letras. Guardado normalizado. | N + D | 1 |
| **D-6** Categorías | 3–40, números cuando tengan sentido ("Turismo 4x4"), signos habituales, sin duplicados normalizados. | D | 1 |
| **D-7** Contraseñas | Mínimo 10 para nuevas o modificadas; las existentes y las temporales no cambian; el inicio de sesión no exige mínimo. | D | 1 (cambio de contraseña), 3 (registro de turistas) |
| **D-8 / C-6** Precios | **> 0** (no existe lógica de servicios gratuitos), tope **técnico** 9.999.999.999,99 [T ✔], decimales por moneda [N ⚠]. Sin topes por moneda ni conversiones. | D + T + N | 2 |
| **D-9 / C-8** Datos existentes | Validación sólo en altas y modificaciones; nada se corrige automáticamente. **No** se crea un endpoint para editar NIT: primero se identifican los registros y se corrigen con el procedimiento de §14. | — | 0 y 1 |
| **C-9** Nombres de personas | 2–100, con espacios, tildes, apóstrofes y guiones; sin restricciones excesivas. | T + D | 1 (cuentas), 3 (registro) |
| **C-10** Consultas | Sólo `SELECT` en **staging**, tras verificar explícitamente entorno y base. Nunca producción, migraciones ni escrituras. | — | 0 |
| **D-10** Alcance | Fase 1: empresas, administradores, categorías y manejo centralizado de errores. | — | — |
| **D-11** Borrar categorías | Impedido si la usan experiencias, paquetes o preferencias de turistas, con mensaje explicativo. | D | 1 |

---

## 3. Fundamento: NIT

| Afirmación | Origen | Estado | Fuente |
|---|---|---|---|
| El NIT de persona natural nacida en Bolivia es la CI + código **01** + un número aleatorio del 1 al 9. | N | ✔ | SIAT Info, *Generación del NIT* |
| El NIT de persona jurídica lo genera el SIN; sus últimos tres dígitos incluyen el código **02** + aleatorio 1–9. Extranjeros con cédula de extranjero: **04** + aleatorio. | N | ✔ | ídem |
| Sociedades comerciales registradas en SEPREC: el NIT es el número de la Matrícula de Comercio. | N | ✔ | ídem y *Finalización* |
| En la factura electrónica `nitEmisor` es **numérico**; el documento del comprador es alfanumérico y admite un complemento del SEGIP. | N | ✔ | SIAT Info, *Factura de Compra y Venta* |
| El NIT/CI es un "dato numérico de máximo 12 dígitos". | N | ✔ | SIN, *Especificación Técnica del Código de Control v7.0* |
| Mínimo de dígitos | — | **No hay fuente oficial legible.** El 5 es decisión de diseño (C-1). | — |
| Dígito verificador | — | ⚠ ninguno publicado | — |

**Por qué no se valida la terminación 01/02/04:** el SIAT describe cómo se generan los NIT **hoy**, pero no dice
que todos los vigentes la sigan; exigirla podría rechazar NIT antiguos legítimos [D].

**Alcance:** validación **de formato**. Comprobar que el NIT exista y esté activo requiere el Padrón del SIN y queda
fuera de la tesis.

---

## 4. Fundamento: teléfonos

Fuente: **Plan Técnico Fundamental de Numeración (PTFN)**, Resolución Ministerial N° 339 del Ministerio de Obras
Públicas, Servicios y Vivienda, 28/12/2021 (lo elabora y fiscaliza la ATT). Todo [N ✔] salvo indicación.

| Regla | PTFN |
|---|---|
| Indicativo de país | 591 (§4 d) |
| Número nacional | **8 dígitos**, sin letras ni símbolos (§5) |
| Fijos | `APQRMCDU`; A = **2** (La Paz, Oruro, Potosí), **3** (Santa Cruz, Beni, Pando), **4** (Cochabamba, Chuquisaca, Tarija); P ∈ {2, 3, 4, 5, 6, 8, 9}; excluidos de PQR: 501–509, 800–809, 900–909 y 911 (Cuadros 1 y 3) |
| Celulares | A = **5, 6 o 7** (Cuadro 4). Prefijos en uso y reservados en el Cuadro 7; **por C-4 no se restringe a los en uso** |
| Voz sobre Internet | prefijo 50 (dentro de la zona 5) |
| Cobro revertido | **9 dígitos**, `800` + 6 dígitos (Cuadros 10 y 11). Los 900–909 (valor agregado, tarifa especial) no se aceptan como contacto [D] |
| Internacional | E.164: hasta **15 dígitos** con el indicativo de país (§3 y §8) |

---

## 5. Fundamento: correos y precios

### 5.1 Correos

| Regla | Origen | Estado | Fuente |
|---|---|---|---|
| Parte local ≤ 64 octetos | T | ✔ | RFC 5321 §4.5.3.1.1 |
| Dominio ≤ 255 octetos | T | ✔ | RFC 5321 §4.5.3.1.2 |
| Dirección ≤ **254** (la ruta de 256 incluye `<` y `>`) | T | ✔ | RFC 5321 §4.5.3.1.3 y errata 1690 de RFC 3696 (verificada) |
| Etiquetas de dominio ≤ 63 | T | ⚠ estándar conocido, no consultado | RFC 1035 |
| Parte local `dot-atom`: `A–Z a–z 0–9 ! # $ % & ' * + / = ? ^ _ { \| } ~ -`; puntos sólo entre caracteres | T | ⚠ estándar conocido, no consultado | RFC 5322 §3.2.3 |
| Sólo ASCII (sin direcciones internacionalizadas) | D | — | C-2 |
| Sin comillas, comentarios ni dominios IP literales; dominio con al menos un punto y TLD de 2+ letras | D | — | Válidos en el estándar, pero raros y propicios a entradas maliciosas |
| Se guardan en minúsculas | T | — | Comportamiento actual; la unicidad de cuentas depende de eso |

### 5.2 Precios (fase 2)

- Columnas `numeric(12,2)`: máximo **9.999.999.999,99** y 2 decimales [T ✔ código]. Hoy el DTO acepta hasta
  `double.MaxValue`, así que un valor mayor termina en 500.
- No existe un concepto de servicio gratuito (ningún campo ni flujo que omita el pago) [T ✔ código] ⇒ **precio > 0**.
- Decimales por moneda: **0 para CLP, PYG y JPY**, 2 para el resto de la lista [N ⚠ fuentes secundarias de ISO 4217].
- Sin topes por moneda ni conversiones (C-6).

---

## 6. Normalización y claves de comparación

Una sola implementación por concepto, **idéntica en C# y TypeScript**, verificada con un archivo común de casos
(§11.1). Todo se aplica **antes** de validar y es lo que se guarda.

| Función | Qué hace | Origen |
|---|---|---|
| `texto de una línea` | Unicode NFC; reemplaza espacios Unicode (incluido el no separable) por espacio; colapsa espacios repetidos; recorta extremos; **rechaza** caracteres de control y de formato invisibles (categorías Unicode Cc y Cf, p. ej. espacio de ancho cero) | D |
| `texto multilínea` | Igual, pero conserva saltos de línea (normaliza `\r\n` a `\n`) | D |
| `opcional` | Vacío o sólo espacios ⇒ `null` | D |
| `NIT` | Quita espacios, puntos y guiones | D |
| `teléfono` | Quita espacios, guiones, puntos y paréntesis; interpreta el prefijo (§7.1, R-05) | D |
| `correo` | Recorta extremos; minúsculas; **no** quita espacios internos (se rechazan) | T + D |
| **Clave de unicidad (K1)** | `texto de una línea` + minúsculas + **sin tildes ni diéresis** (á→a, é→e, í→i, ó→o, ú→u, ü→u, y sus mayúsculas y variantes à, â, ä, ã…). **La ñ se conserva** como letra distinta (`Peña` ≠ `Pena`) | D |
| **Clave de similitud (K2)**, sólo empresas | K1 + sin signos de puntuación + sin la forma societaria final (`srl`, `sa`, `ltda`) | D (la lista de formas societarias es ⚠ no verificada contra el Código de Comercio) |

**Alcance de la unicidad (D-4):**

| Entidad | Bloquea (409) | Advierte (no bloquea) | Alcance |
|---|---|---|---|
| Empresa | Misma K1 | Misma K2 con distinta K1 (`Andes Tours S.R.L.` / `Andes Tours SRL` / `Andes Tours`) | Todo el sistema |
| Categoría | Misma K1 (`Aventura` = `aventura` = `Aventúra`; `Turismo 4x4` ≠ `Turismo 4x2`) | — | Todo el sistema |
| Destino (fase 2) | Misma K1 | — | Mismo padre y tipo, como hoy ("La Paz" departamento y ciudad conviven) |
| Cuenta de usuario | Mismo correo (minúsculas) | — | Todo el sistema, sin importar el rol (como hoy) |
| NIT | Mismo valor normalizado | — | Todo el sistema |

---

## 7. Matriz definitiva — fase 1

Columnas "hoy": F = formulario, DTO = DTO + servicio, BD = base. Los mensajes son la redacción propuesta.

### 7.1 Reglas de la fase 1

| Regla | Campo(s) | Hoy (F / DTO / BD) | Regla definitiva | Origen | Mensajes |
|---|---|---|---|---|---|
| **R-01** | Nombre de empresa (alta de operador `companyName`; Mi empresa `name`) | 2–150 / 2–150 sin normalizar / `varchar(150)` | Obligatorio · texto de una línea · **3–100** · **≥2 letras** · letras (con tildes, ñ), dígitos, espacio y `. , & ' ’ - ( ) / + °` · ningún carácter no numérico repetido más de 3 veces seguidas · K1 única · K2 repetida ⇒ advertencia al administrador | D | "El nombre de la empresa debe tener entre 3 y 100 caracteres." · "Incluí al menos dos letras." · "El nombre tiene caracteres no permitidos: «<»." · "Ya existe una empresa con ese nombre: «Andes Tours SRL»." · Advertencia: "Hay una empresa con un nombre parecido: «Andes Tours S.R.L.». Verificá que no sea la misma." |
| **R-02** | Descripción de empresa | ≤2000 alta / ≤1000 edición (error invisible) / `text` | Opcional · multilínea · **≤1000** en alta y edición | D | "La descripción puede tener hasta 1000 caracteres (tiene 1043)." |
| **R-03** | NIT (`legalDocument`, sólo alta) | 3–50 cualquier carácter / único exacto / `varchar(50)` + único | Obligatorio · normalización NIT · **sólo dígitos** · **5–12** · no un único dígito repetido · único sobre el valor normalizado · no editable (como hoy) | N (dígitos, máx. 12 ✔) · D (mín. 5, repetidos) | "El NIT sólo puede tener números." · "El NIT debe tener entre 5 y 12 dígitos." · "Ya existe una empresa con ese NIT." |
| **R-04** | Correo de contacto; correo de acceso de operador y de administrador | `email()` / `EmailAddress` ≤150 o ≤255 / `varchar(255)` | Obligatorio · normalización correo · ASCII · sin espacios internos · **≤254**, local ≤64, etiquetas ≤63 · formato de §5.1 · **cuentas: únicas** (409); contacto de empresa: no único | T ✔ (largos) · D (ASCII, formato estricto) | "El correo no puede tener espacios." · "Usá sólo letras sin tildes, números y . _ - + en el correo." · "El correo puede tener hasta 254 caracteres." · "Ya existe una cuenta con ese correo." |
| **R-05** | Teléfono de contacto | ≤30 (error invisible) / ≤30 / `varchar(30)` | Opcional · sin letras · normalización teléfono, luego: **(a)** empieza con `+` y no es `+591` ⇒ internacional `^\+[1-9]\d{6,14}$`, se guarda igual · **(b)** `+591` o `591` + resto, o sin prefijo ⇒ nacional: fijo `^[234](?!50[1-9]\|80\d\|90\d\|911)[2-689]\d{6}$`, celular `^[567]\d{7}$` o línea `^800\d{6}$`; se guarda `+591…` · **(c)** empieza con `00` o con `0`+código de operador ⇒ rechazo con indicación | N ✔ (largos y prefijos de Bolivia, E.164) · D (prefijo opcional, formato guardado) | "El teléfono no puede tener letras." · "Los números de Bolivia tienen 8 dígitos (o 9 las líneas 800)." · "No es un número de Bolivia válido: los fijos empiezan con 2, 3 o 4 y los celulares con 5, 6 o 7." · "Para un número del exterior escribí el + y el código de país, por ejemplo +54 11 1234 5678." · "Escribí el número sin el 0 ni el código de operador." |
| **R-06** | Nombre y apellido de cuentas (operador y administrador) | 2–100 / 2–100 / `varchar(100)` | Obligatorio · texto de una línea · **2–100** · `^\p{L}[\p{L}\p{M} '’\-.]*$` (letras con tildes, espacios, apóstrofes, guiones y punto para abreviaturas como "Ma.") | T (largos) · D (caracteres) | "El nombre sólo puede tener letras, espacios, apóstrofes y guiones." |
| **R-07** | Nombre de categoría | botón deshabilitado si <2 / 2–100 único exacto / `varchar(100)` + único | Obligatorio · texto de una línea · **3–40** · **≥2 letras** · letras, dígitos, espacio y `& - ' ’ , . / ( )` · regla de repeticiones de R-01 · K1 única · se guarda como lo escribió el administrador | D | "El nombre de la categoría debe tener entre 3 y 40 caracteres." · "Ya existe la categoría «Aventura»." |
| **R-08** | Descripción de categoría | sin límite / ≤500 / `text` | Opcional · una línea · **≤300** | D | "La descripción puede tener hasta 300 caracteres." |
| **R-09** | Borrado de categoría | confirmación / borra sin verificar / FK `CASCADE` | Si la usan experiencias, paquetes o preferencias ⇒ **409** con conteos; FK `RESTRICT` cubre la carrera entre verificación y borrado | D | "No se puede eliminar «Aventura» porque está asignada a 4 experiencias y 1 paquete, y 12 turistas la eligieron como interés. Quitala de esos elementos antes de eliminarla." |
| **R-10** | Motivo de rechazo de empresa | ≥5 sin máximo / 5–500 / `text` | Obligatorio · multilínea · **5–500** (sin cambios en el backend) · contador visible | T | "El motivo debe tener entre 5 y 500 caracteres." |
| **R-11** | Registro para venta de vuelos | ≤100, obligatorio si autoriza | Obligatorio al autorizar · texto de una línea · **3–100** · letras, dígitos, espacio y `- . / °` | T (largo) · D | "Indicá el registro o la licencia (3 a 100 caracteres)." |
| **R-12** | Cambio de contraseña | ≥10 / 10–200 | **10–200** (sin cambios de límites), distinta de la actual, confirmación igual; mensajes en español | T + D | "Usá al menos 10 caracteres." |
| **R-13** | Inicio de sesión | correo válido / no vacío | Correo: recorte, ≤254; contraseña: no vacía, **sin mínimo** | T + D | "Ingresá tu correo." · "Ingresá tu contraseña." |
| **R-14** | Errores (todos los endpoints) | inglés, banner, 500 por base | §9.2 | D | — |

### 7.2 Formularios y endpoints afectados en la fase 1

| Formulario (backoffice) | Endpoint | Reglas |
|---|---|---|
| Alta de operador | `POST /api/admin/provider-accounts` | R-01…R-06, advertencia K2 |
| (nuevo) Verificación de nombre | `GET /api/admin/companies/name-check?name=&excludeId=` (sólo ADMIN, sólo lectura) | K1 / K2 |
| Mi empresa | `PUT /api/companies/me` | R-01, R-02, R-04 (contacto), R-05 |
| Alta de administrador | `POST /api/admin/admin-accounts` | R-04, R-06 |
| Categorías | `POST`/`PUT`/`DELETE /api/admin/categories` | R-07…R-09 |
| Rechazo de empresa | `POST /api/admin/companies/{id}/reject` | R-10 |
| Venta de vuelos | `PUT /api/admin/companies/{id}/flight-seller` | R-11 |
| Cambio de contraseña / login | `POST /api/auth/change-password`, `POST /api/auth/login` | R-12, R-13 |
| Todos | — | R-14 |

**Datos viejos al editar:** Mi empresa valida los campos que el formulario envía (nombre, descripción, correo,
teléfono). Si un dato guardado no cumple, el formulario lo marca y pide corregirlo antes de guardar. El NIT no se
envía al editar, así que un NIT viejo inválido **no** bloquea la edición del resto ni las acciones del administrador
(aprobar, suspender).

---

## 8. Matriz de las fases 2 y 3

Propuestas; se confirman al empezar cada fase.

| Fase | Módulo | Reglas principales | Origen |
|---|---|---|---|
| 2 | Destinos | Nombre 2–80, caracteres de nombre propio; K1 única por padre y tipo; imagen sólo `https://` | D |
| 2 | Experiencias y paquetes | Título 5–120; descripción 20–5000; incluye/excluye ≤2000; condiciones ≤3000; **precio > 0, ≤ 9.999.999.999,99, decimales por moneda**; duración en minutos 1–10 080, en días 1–90; ≤10 imágenes `https://`; ≤5 categorías; mensajes sin nombres internos | D + T + N⚠ |
| 2 | Disponibilidad | Sin fechas pasadas; carga masiva ≤366 días | D |
| 2 | Vuelos de operador y motivos | Caracteres de `CarrierName`; mensajes en español en los `Range` | D |
| 3 | App móvil: registro | R-06 para nombres; R-04 para correo; **contraseña 10–200** (D-7) en DTO y app a la vez | D |
| 3 | App móvil: pasajeros | Aplicar en la app los máximos del servidor; teléfono E.164 internacional como hoy | T |
| 3 | App móvil: errores | Adoptar el traductor compartido | D |

---

## 9. Errores: situación actual y diseño

### 9.1 Situación actual

| Origen | Forma | Idioma | Ejemplo |
|---|---|---|---|
| DTO inválido (automático por `[ApiController]`) | `ValidationProblemDetails` | **Inglés** | `"One or more validation errors occurred."`, `"The CompanyName field is required."` |
| JSON mal formado | ídem, clave `$.campo` | **Inglés técnico** | `"The JSON value could not be converted to System.Guid. Path: $.destinationId…"` |
| Mensajes propios | ídem | Español con nombres internos | `"DurationDays debe estar entre 1 y 90."` |
| Excepciones de negocio | `ProblemDetails.detail` | Español, a veces con estados internos | `"(estado actual: PENDING_APPROVAL)"` |
| Unicidad / `CHECK` / rango numérico | **500** | Genérico | `"Ocurrió un error inesperado…"` |

Backoffice: `getErrorMessage` concatena los mensajes sin filtrar y cae a `error.message` de Axios (`"Network Error"`);
todo en un banner. Esquemas zod sin mensaje ⇒ inglés. Errores de descripción y teléfono no se renderizan.
App móvil: mejor resuelta, pero deja pasar los mensajes por defecto en inglés.

### 9.2 Diseño del manejo centralizado (R-14)

**Contrato de error (backend)**

```json
{
  "type": "about:blank",
  "title": "Hay datos para corregir.",
  "status": 400,
  "errorCode": "VALIDATION_FAILED",
  "errors": {
    "legalDocument": ["El NIT debe tener entre 5 y 12 dígitos."],
    "contactPhone": ["El teléfono no puede tener letras."]
  },
  "traceId": "…"
}
```

- 400 de modelo: `InvalidModelStateResponseFactory` propio. Claves en **camelCase** iguales a las del formulario;
  claves `$.campo` de deserialización ⇒ campo correspondiente con "El valor de este campo no es válido.".
  Cualquier mensaje que no sea de los atributos propios (red de seguridad) se reemplaza por uno genérico en español.
- 409 por duplicado: `errorCode: "DUPLICATE"`, `detail` y `errors` con el campo (`name`, `legalDocument`, `email`).
- 409 por categoría en uso: `errorCode: "CATEGORY_IN_USE"`, `detail` con los conteos.
- `GlobalExceptionHandler`: `23505` ⇒ 409 (campo según el nombre de la restricción); `23514`, `22001`, `22003` ⇒
  400 "Algún dato no cumple las reglas. Revisá el formulario."; nunca 500 por un dato inválido.
- Mensajes sin nombres de propiedades ni estados internos ("pendiente de aprobación", no `PENDING_APPROVAL`).

**Frontend (backoffice en la fase 1)**

- `@turisclick/validation`: esquemas con mensajes en español, traductor de errores HTTP (basado en el `toApiError`
  del móvil) y `applyServerErrors(form, error)`, que ubica cada error del servidor bajo su campo.
- `z.config(z.locales.es())` al iniciar la app como red de seguridad.
- Componente `FormField` que siempre muestra el error del campo.
- Validación al salir del campo y al enviar (`mode: 'onTouched'`); contadores de caracteres.

---

## 10. Plan definitivo de la fase 1

### 10.1 Orden de trabajo

| Paso | Contenido | Condición |
|---|---|---|
| 0 | Diagnóstico de datos en staging (§13) | Tu confirmación de la base (Q-1) |
| 1 | Reglas compartidas: backend `Shared/Validation` y paquete `@turisclick/validation`, con el archivo común de casos | — |
| 2 | Manejo centralizado de errores (backend + traductor del backoffice) | — |
| 3 | Empresas y cuentas (alta de operador, Mi empresa, alta de administrador, rechazo, venta de vuelos, login, cambio de contraseña) | — |
| 4 | Categorías (alta, edición, borrado protegido) | — |
| 5 | Migración `0016` escrita y probada **sólo en la base de tests** | No se aplica a ninguna otra base sin tu aprobación |
| 6 | Datos de prueba, demo y Postman con NIT numéricos | — |
| 7 | Pruebas completas, revisión tuya, y recién entonces commit | Tu aprobación |

### 10.2 Archivos — backend

| Archivo | Cambio |
|---|---|
| `src/TurisClick.Api/Shared/Validation/ValidationLimits.cs` (nuevo) | Constantes de §7.1 |
| `src/TurisClick.Api/Shared/Validation/TextNormalizer.cs` (nuevo) | Funciones de §6, K1 y K2 |
| `src/TurisClick.Api/Shared/Validation/NitRules.cs`, `PhoneRules.cs`, `EmailRules.cs` (nuevos) | Normalización + validación + mensajes |
| `src/TurisClick.Api/Shared/Validation/Attributes/*.cs` (nuevos) | `[OrganizationName]`, `[PersonName]`, `[CategoryName]`, `[BolivianNit]`, `[ContactPhone]`, `[StrictEmail]`, `[PlainText(max, multiline)]`: validan **el valor normalizado** con mensaje en español |
| `src/TurisClick.Api/Shared/Validation/ValidationProblemFactory.cs` (nuevo) | Respuesta 400 de §9.2 |
| `src/TurisClick.Api/Shared/Validation/NameKeySql.cs` (nuevo) | La expresión SQL de K1, única fuente para el índice y las consultas |
| `src/TurisClick.Api/Program.cs` | `ConfigureApiBehaviorOptions(InvalidModelStateResponseFactory)` |
| `src/TurisClick.Api/Shared/Exceptions/AppExceptions.cs` | Campo opcional en `ConflictAppException`/`ValidationAppException`; códigos `DUPLICATE`, `CATEGORY_IN_USE`, `VALIDATION_FAILED` |
| `src/TurisClick.Api/Shared/Exceptions/GlobalExceptionHandler.cs` | `errors` por campo; `23505`, `23514`, `22001`, `22003` |
| `src/TurisClick.Api/Modules/Admin/Dtos/AdminOnboardingDtos.cs` | Atributos nuevos en `CreateProviderAccountRequest` y `CreateAdminAccountRequest` |
| `src/TurisClick.Api/Modules/Admin/Services/AdminOnboardingService.cs` | Normalizar antes de guardar; unicidad K1 y NIT normalizado; `23505` ⇒ 409 también en el alta de operador |
| `src/TurisClick.Api/Modules/Companies/Dtos/UpdateCompanyRequest.cs`, `RejectCompanyRequest.cs` | Atributos y mensajes |
| `src/TurisClick.Api/Modules/Companies/Dtos/CompanyNameCheckResponse.cs` (nuevo) | `{ exactMatch, similar[] }` |
| `src/TurisClick.Api/Modules/Companies/Services/CompanyService.cs` (+ interfaz) | Normalizar; unicidad K1 excluyendo la propia; `CheckNameAsync`; mensajes sin estados internos |
| `src/TurisClick.Api/Modules/Companies/Repositories/CompanyRepository.cs` (+ interfaz) | Consulta por K1 con la expresión del índice; nombres para K2 |
| `src/TurisClick.Api/Modules/Companies/Controllers/AdminCompaniesController.cs` | `GET name-check` |
| `src/TurisClick.Api/Modules/Categories/Dtos/CreateCategoryRequest.cs`, `UpdateCategoryRequest.cs` | Atributos |
| `src/TurisClick.Api/Modules/Categories/Services/CategoryService.cs`, `Repositories/CategoryRepository.cs` (+ interfaces) | K1; conteo de usos; 409 `CATEGORY_IN_USE`; `23505`/`23503` ⇒ 409 |
| `src/TurisClick.Api/Modules/Experiences/Persistence/ExperienceConfiguration.cs`, `Packages/Persistence/PackageConfiguration.cs`, `Preferences/Persistence/TouristPreferenceConfiguration.cs` | FK hacia `categories` con `DeleteBehavior.Restrict` |
| `src/TurisClick.Api/Modules/Auth/Dtos/LoginRequest.cs`, `ChangePasswordRequest.cs` | Mensajes en español; correo ≤254 |
| `src/TurisClick.Api/Modules/Flights/ProviderFlights/Dtos/ProviderFlightDtos.cs` | Sólo `FlightSellerAuthorizationRequest.Registration` (R-11) |
| `src/TurisClick.Api/Infrastructure/Database/Migrations/<fecha>_0016_Fase1_Validaciones.cs` (+ `Designer` y snapshot) | §12.2 |

### 10.3 Archivos — frontend y herramientas

| Archivo | Cambio |
|---|---|
| `frontend/packages/validation/` (nuevo): `package.json`, `src/index.ts`, `limits.ts`, `normalize.ts`, `nit.ts`, `phone.ts`, `email.ts`, `schemas.ts`, `messages.ts`, `httpErrors.ts`, `applyServerErrors.ts` | Reglas compartidas; `zod` como dependencia par |
| `frontend/package-lock.json` | Enlace del nuevo workspace (ver riesgo RS-9) |
| `frontend/apps/backoffice/package.json` | Dependencia `@turisclick/validation` |
| `frontend/apps/backoffice/vite.config.ts` | Incluir las pruebas del paquete en Vitest, si no se le agrega un ejecutor propio |
| `frontend/apps/backoffice/src/main.tsx` | `z.config(z.locales.es())` |
| `frontend/apps/backoffice/src/lib/errors.ts` | Usar el traductor compartido (misma firma para no tocar los demás módulos) |
| `frontend/apps/backoffice/src/components/form/FormField.tsx` (nuevo) | Etiqueta + control + error + contador |
| `frontend/apps/backoffice/src/modules/companies/NewProviderAccountPage.tsx`, `api.ts`, `RejectDialog.tsx`, `FlightSellerCard.tsx` | Esquemas compartidos, errores por campo, advertencia K2 |
| `frontend/apps/backoffice/src/modules/my-company/MyCompanyPage.tsx` | ídem |
| `frontend/apps/backoffice/src/modules/administrators/NewAdminAccountPage.tsx` | ídem |
| `frontend/apps/backoffice/src/modules/categories/CategoriesPage.tsx` | Formulario con esquema; mensaje de categoría en uso |
| `frontend/apps/backoffice/src/auth/LoginPage.tsx`, `ChangePasswordPage.tsx` | Esquemas compartidos |
| `frontend/packages/api-client/src/modules/companies.ts` (+ `generated/schema.ts` regenerado) | Endpoint `name-check` |
| `tools/demo-catalog/catalog.mjs`, `validate-e2e.mjs` | NIT numéricos ficticios (sólo archivos; ninguna base se recarga) |
| `postman/TurisClick.postman_collection.json` | 10 NIT de la forma `NIT-…` ⇒ numéricos |

**No se tocan** en la fase 1: app móvil, Azure, Terraform, GitHub Actions, módulos de destinos, experiencias,
paquetes, reservas y vuelos (salvo R-11).

---

## 11. Pruebas de la fase 1

### 11.1 Archivo común de casos

`tests/contract/validation-vectors.json` (nuevo): entradas y resultados esperados (normalizado, válido o inválido,
mensaje, K1, K2) para NIT, teléfono, correo, nombres y claves. Lo leen **las pruebas de C# y las de TypeScript**,
así ambas implementaciones no pueden divergir. Incluye, como mínimo, por regla: válido típico, vacío, sólo espacios,
`null` en opcionales, en el límite, límite + 1, caracteres no permitidos, caracteres de control y de ancho cero,
Unicode legítimo (tildes, ñ, `’`), e intentos de inyección (`<script>`, `' OR 1=1 --`).

Ejemplos: NIT `1.234.567-01-1` ⇒ `1234567011` válido; `1234` inválido; `1234567890123` inválido; `11111` inválido.
Teléfono `+591 7 123-4567` ⇒ `+59171234567`; `22123456` ⇒ `+59122123456`; `800 10 1234` ⇒ `+591800101234`;
`+54 11 1234 5678` ⇒ `+541112345678`; `7ABC4567` inválido; `0 10 22123456` inválido. Correo `  Ana@Ejemplo.BO ` ⇒
`ana@ejemplo.bo`; `ana maria@x.bo`, `josé@x.bo`, `a..b@x.bo`, local de 65 caracteres inválidos. K1:
`Andes  Tóurs` ⇒ `andes tours`; `Peña` ≠ `Pena`. K2: `Andes Tours S.R.L.` = `Andes Tours SRL`.

### 11.2 Backend

| Proyecto / archivo | Qué prueba |
|---|---|
| `tests/TurisClick.Api.Tests/Shared/Validation/*Tests.cs` (nuevos) | Normalizadores, reglas y atributos contra el archivo común |
| `tests/TurisClick.Api.Tests/Shared/GlobalExceptionHandlerTests.cs` | `23505`/`23514`/`22001`/`22003` ⇒ 409/400 en español; nunca 500 |
| `tests/TurisClick.Api.Tests/Shared/ValidationProblemFactoryTests.cs` (nuevo) | Título en español, claves camelCase, mensajes técnicos reemplazados |
| `tests/TurisClick.Api.Tests/Modules/Categories/CategoryServiceTests.cs`, `Modules/Companies/CompanyServiceTests.cs` | K1, exclusión de la propia entidad, conteo de usos |
| `Integration/AdminAccountsTests.cs` | Alta de operador y de administrador por **llamada directa a la API**: válidos, vacíos, largos, formatos, NIT con separadores guardado normalizado, duplicados por mayúsculas/tildes/NIT ⇒ 409 con campo, similar por puntuación ⇒ 201, dos altas simultáneas ⇒ un 201 y un 409 (nunca 500), name-check |
| `Integration/CompaniesEndpointsTests.cs` | Mi empresa: mismas reglas; renombrar a una K1 ajena ⇒ 409; conservar el propio ⇒ 200; empresa con NIT viejo inválido insertada directo en la base ⇒ se edita y se aprueba sin error |
| `Integration/AdminCompaniesEndpointsTests.cs` | Rechazo (R-10) y name-check sólo para ADMIN |
| `Integration/CategoriesEndpointsTests.cs` | Alta/edición: reglas, "Turismo 4x4", duplicados ⇒ 409, concurrencia; borrado en uso por experiencia, paquete o preferencia ⇒ 409 y nada borrado; sin uso ⇒ 204 |
| `Integration/AuthEndpointsTests.cs` | Login con contraseña existente de 8 caracteres sigue funcionando; cambio de contraseña <10 ⇒ 400 en español |
| `Integration/ErrorDisclosureAndHealthTests.cs` | JSON mal formado ⇒ 400 en español sin `System.`/`$.`; ninguna respuesta de validación contiene texto en inglés |
| `Integration/ValidationPersistenceTests.cs` (nuevo) | **Persistencia, sin pasar por la API**: insertar por SQL dos empresas o categorías con la misma K1 ⇒ `23505` con el nombre del índice; borrar por SQL una categoría referenciada ⇒ `23503`; la migración `0016` existe y quedó aplicada |
| Datos de prueba | Los generadores `NIT-{Guid}`, `DOC-{suffix}` pasan a NIT numéricos únicos |

Las pruebas de integración corren contra la **base de tests** de `ConnectionStrings:TestDatabase` (nunca V1, V2 ni
staging), que la fábrica migra automáticamente.

### 11.3 Frontend

| Archivo | Qué prueba |
|---|---|
| `frontend/packages/validation/__tests__/*.test.ts` (nuevos) | Mismo archivo común; mensajes en español; `applyServerErrors`; traductor de errores (sin `Network Error`, sin texto en inglés, sin `detail` de 5xx) |
| `frontend/apps/backoffice/src/modules/companies/__tests__/NewProviderAccountPage.test.tsx` (nuevo) | Error bajo el campo antes de enviar; 400/409 del servidor en el campo correcto; advertencia de nombre parecido no bloquea |
| `…/my-company/__tests__/MyCompanyPage.test.tsx` (nuevo) | Errores de descripción y teléfono visibles; dato viejo inválido marcado |
| `…/administrators/__tests__/NewAdminAccountPage.test.tsx` | Reglas R-04 y R-06 |
| `…/categories/__tests__/CategoriesPage.test.tsx` (nuevo) | Reglas R-07/R-08; mensaje de categoría en uso |
| `…/lib/__tests__/errors.test.ts` | Traductor nuevo |
| `…/__tests__/noEnglishValidationMessages.test.ts` (nuevo, estático) | Ningún `.min(`/`.max(` sin mensaje en los esquemas; ningún atributo de validación sin mensaje en los DTO de la fase 1 |
| `…/companies/__tests__/CompanyDetailPage.test.tsx` | Fixture `30-12345678-9` ⇒ NIT numérico |

### 11.4 Comandos

```bash
dotnet test TurisClick.slnx
```

```bash
npm test --workspace apps/backoffice
```

```bash
npm test --workspace apps/tourist-mobile
```

(La app móvil no cambia en la fase 1; su suite se corre para confirmar que el nuevo formato de error no la afecta:
`toApiError` sólo lee los valores de `errors`, no sus claves.) Además, `tsc -b` y `oxlint` del backoffice.

---

## 12. PostgreSQL: impacto e incompatibilidades

### 12.1 Decisiones de diseño de base

| Tema | Opción elegida | Por qué |
|---|---|---|
| Unicidad sin mayúsculas ni tildes | **Índice único por expresión** sobre K1, p. ej. `CREATE UNIQUE INDEX uq_companies_name_key ON companies ((translate(lower(regexp_replace(btrim(name), '\s+', ' ', 'g')), 'ÁÀÂÄÃÉÈÊËÍÌÎÏÓÒÔÖÕÚÙÛÜáàâäãéèêëíìîïóòôöõúùûü', 'aaaaaeeeeiiiiooooouuuuaaaaaeeeeiiiiooooouuuu')))` | No agrega columnas ni rellena filas existentes (no escribe datos); la base hace cumplir la regla aun con llamadas directas o concurrentes; no requiere la extensión `unaccent` (que tocaría Azure) |
| Coherencia C# ↔ SQL | La expresión vive en `NameKeySql.cs` y el archivo común de casos verifica que C#, TypeScript y la base den la misma K1 | `lower()` de PostgreSQL depende de `lc_ctype` para letras no ASCII; las vocales acentuadas, en mayúscula o minúscula, se resuelven con `translate` explícito y no dependen de la configuración |
| `CHECK` de formato | **No se agregan en la fase 1** | Un `CHECK ... NOT VALID` no revisa filas viejas al crearse, **pero sí cualquier `UPDATE` posterior de esas filas**: aprobar o suspender una empresa con NIT viejo inválido fallaría. Se agregan después de corregir los datos (§14) |
| Categorías en uso | FK `ON DELETE RESTRICT` en las tres tablas intermedias | Protege incluso ante borrados directos o concurrentes |
| NIT normalizado | Se guarda normalizado; el servicio compara contra los valores viejos normalizados con `regexp_replace`; índice por expresión sobre el NIT **sólo si** la fase 0 no encuentra colisiones | Evita que `1234-567` viejo y `1234567` nuevo convivan |
| Tamaños de columna | **Sin cambios** | `varchar(150)` ≥ 100, `varchar(255)` ≥ 254, `varchar(30)` ≥ 16 (E.164 con `+`), `varchar(100)` ≥ 40 |

### 12.2 Migración `0016_Fase1_Validaciones` (escrita en la fase 1, aplicada sólo con tu aprobación)

1. **Verificación previa** (bloque `DO`): si hay empresas o categorías con K1 repetida, la migración se detiene con
   un mensaje en español que dice cuáles, **sin aplicar nada** (la migración es transaccional).
2. FK de `experience_categories`, `package_categories` y `tourist_preference_categories` hacia `categories`: se
   eliminan y se recrean con `RESTRICT` (las FK hacia experiencias, paquetes y preferencias no cambian).
3. `uq_companies_name_key` y `uq_categories_name_key` (índices por expresión).
4. Índice por expresión sobre el NIT normalizado, si la fase 0 lo permite (Q-3).
5. `Down`: revierte los índices y vuelve las FK a `CASCADE`.

Los índices por expresión se escriben con `migrationBuilder.Sql(...)` porque EF Core no los modela; se documentan en
la configuración de cada entidad para que nadie los borre por accidente.

### 12.3 Incompatibilidades e impacto

| # | Cambio | Choque o impacto | Mitigación |
|---|---|---|---|
| P-1 | Índices únicos por K1 | **La creación falla** si ya hay duplicados normalizados | Fase 0 los detecta; la verificación previa aborta con un mensaje claro; se resuelven con §14 antes de aplicar |
| P-2 | Índices por expresión | Requieren funciones `IMMUTABLE` (`lower`, `btrim`, `regexp_replace`, `translate` lo son); si no, la migración falla en la base de tests, no en otra | Se prueba primero en la base de tests |
| P-3 | Bloqueo al crear índices | `CREATE INDEX` bloquea escrituras en la tabla mientras se construye | Tablas chicas (decenas de filas): bloqueo de milisegundos |
| P-4 | FK `RESTRICT` | Cambia el comportamiento: borrar una categoría usada deja de borrar sus asignaciones | Es lo pedido en D-11; el servicio lo explica antes de llegar a la base |
| P-5 | NIT viejos inválidos | No chocan con la fase 1 (no hay `CHECK`); el alta nueva no puede repetir un NIT viejo normalizado | Corrección según §14 |
| P-6 | Nombres viejos que no cumplen R-01 | Se siguen mostrando; al editar, el formulario pide corregirlos | — |
| P-7 | `lc_ctype` de la base | `lower()` de letras no ASCII fuera de la tabla (p. ej. `Ç`) puede diferir entre C# y PostgreSQL | La fase 0 consulta `lc_ctype`; la diferencia sólo afecta caracteres poco comunes |
| P-8 | Contraseñas | Ningún choque: no se tocan hashes ni el login | — |
| P-9 | Snapshot de EF | Debe reflejar las FK `RESTRICT`; los índices por expresión no figuran en el modelo | Revisión del snapshot generado |

---

## 13. Fase 0: diagnóstico en staging (sólo lectura)

### 13.1 Qué es "staging" según el repositorio

| Base | Dónde | ¿Es staging? |
|---|---|---|
| `turisclick_staging_o19` | **PostgreSQL local** (`localhost`), creada en la Oleada 19 por `docs/evidencia/oleada19/staging_db.py`, que aborta si el host no es local | **Candidata.** Su README (`docs/evidencia/oleada19/README.md`) dice que se iba a borrar: puede no existir |
| `turisclick_db_staging` | PostgreSQL local, **propuesta** en `docs/plan-staging-turisclick.md` | Candidata si se creó; el documento dice "Nada ejecutado" |
| `turisclick_db_v2` | Azure (`turisclick-postgres-jpm`), la que usa `app-turisclick-v2-api` | **No es staging.** Es el entorno desplegado: no se consulta (C-10) |
| `turisclick_db` | Azure, V1 | No: no se consulta |
| `turisclick_v2_dev`, `turisclick_v2_test` | Bases de desarrollo y de tests | No son staging |

#### Verificación del 17/11/2026 (Q-1)

Con tu autorización se listaron las bases del **PostgreSQL local**, con el mismo método que
`docs/evidencia/oleada19/staging_db.py`: se leyó sólo `ConnectionStrings:DefaultConnection` de los user-secrets de
la API (sin mostrarla), se comprobó que el host fuera `localhost` y se abrió una sesión **de sólo lectura**
(`default_transaction_read_only=on`) sobre la base de mantenimiento `postgres`, sin conectarse a ninguna base de
TurisClick.

- Identidad de la sesión: base `postgres`, servidor `::1` (local), puerto 5432, `transaction_read_only = on`,
  PostgreSQL 16.11.
- **`turisclick_staging_o19`: existe** (dueño `postgres`, ~10 MB).
- **`turisclick_db_staging`: no existe.**
- Otras bases locales, que no se tocaron: `turisclick_db`, `turisclick_v2_dev`, `turisclick_v2_demo`,
  `turisclick_v2_test` y varias `turisclick_v2_test_*`, `turisclick_b2_migration_check`, `turisclick_d_e2e`.
- No se consultó el contenido de ninguna base: no se sabe todavía qué migraciones tiene aplicadas
  `turisclick_staging_o19` ni qué datos contiene.

**¿Es adecuada `turisclick_staging_o19`?** Es local y está aislada (su script se negaba a crearla fuera de
`localhost` y la Oleada 19 probó que sus tokens no sirven en Azure V2). Pero su README la describe como
**temporal** ("se va a borrar"), sus datos son los que dejó la batería de la Oleada 19 y no se sabe si tiene la
última migración (`0015_Oleada18_VuelosDeOperador`). Opciones:

| Opción | Qué implica | Autorización que hace falta |
|---|---|---|
| **A. Reutilizarla** | Primero, una única consulta de sólo lectura a esa base: últimas filas de `__EFMigrationsHistory` y **conteos** (no contenidos) de `companies`, `categories` y `users`. Si tiene la migración 0015 y datos útiles, sirve como staging de la fase 0 | Conectarse en sólo lectura a `turisclick_staging_o19` para esa verificación |
| **B. Crear `turisclick_db_staging`** (recomendada si A no sirve) | Base local nueva y aislada, con el procedimiento que sigue | Crear la base y aplicar migraciones **en local** |

#### Opción A ejecutada (17/11/2026)

Con tu autorización, sólo lectura y sólo el alcance aprobado:

- **Identidad:** base `turisclick_staging_o19`, servidor `::1` (local), puerto 5432, `transaction_read_only = on`,
  PostgreSQL 16.11. Se verificó antes de cualquier otra consulta.
- **Migraciones:** las **15** aplicadas, de `0001_Oleada0_Auth` a `0015_Oleada18_VuelosDeOperador`, todas con
  EF Core 10.0.10. Coinciden **exactamente** con las 15 migraciones del repositorio: el esquema está al día.
- **Conteos:** **2 empresas, 6 categorías, 5 usuarios.**
- No se leyó ningún otro dato ni se ejecutó ninguna consulta de §13.3.

**Conclusión:** el **esquema es adecuado** (sirve para ensayar la migración `0016` y para el diagnóstico de §13.3),
pero los **datos son escasos**: con 2 empresas y 6 categorías el diagnóstico va a decir poco, y no reproduce el
catálogo de demo (8 empresas con `NIT-DEMO-…`) ni lo que ve el tutor en Azure. Alternativas en §16 (Q-1b).

#### Procedimiento propuesto para crear un staging local nuevo (opción B, no ejecutado)

1. **Comprobaciones previas (sólo lectura):** el host de la cadena es `localhost`/`127.0.0.1`/`::1`; la sesión de
   identidad responde servidor local; `turisclick_db_staging` **no** existe y no está en la lista prohibida
   (`turisclick_db`, `turisclick_db_v2`, `turisclick_v2_dev`, `turisclick_v2_test`). Si algo falla, se aborta.
2. **Crear la base** (única escritura, en el servidor local): `CREATE DATABASE turisclick_db_staging;`
3. **Aplicar las migraciones existentes** (las 15 actuales, no la `0016`) **sólo a esa base**:
   `dotnet ef database update --project src/TurisClick.Api --connection "<cadena local con Database=turisclick_db_staging>"`.
   La cadena se arma en memoria a partir de los user-secrets, sólo cambiando el nombre de la base; no se modifican
   los user-secrets ni ningún archivo. Antes de ejecutar se muestra el destino (host, puerto, base) y se aborta si
   la base no es exactamente `turisclick_db_staging`.
4. **Verificar:** sesión de identidad sobre la base nueva; `__EFMigrationsHistory` con las 15 migraciones.
5. **Datos (paso aparte, a decidir):** una base recién migrada está vacía. Para que el diagnóstico tenga sentido
   hay que cargar datos representativos; la opción natural es el catálogo de demo de `tools/demo-catalog` contra
   una API local apuntada a esa base, que reproduce los NIT `NIT-DEMO-…`. Ese paso requiere levantar la API local
   con esa base y una cuenta de administrador local; se detalla y se pide autorización por separado.
6. **Deshacer:** si hace falta, `DROP DATABASE turisclick_db_staging;` con la misma comprobación de nombre y host.
   No afecta ninguna otra base.

#### Respaldo de `turisclick_staging_o19` (ejecutado el 17/11/2026, opción 2 de Q-1b)

Antes del respaldo se repitió la verificación de identidad: base `turisclick_staging_o19`, servidor `::1`, puerto
5432, sesión de sólo lectura, 15 migraciones. `pg_dump` sólo lee; no se restauró nada ni se creó ninguna base.

| Dato | Valor |
|---|---|
| Archivo | `C:\Users\Juan Pablo\turisclick-backups\validaciones-fase1\turisclick_staging_o19-antes-de-catalogo-demo-20261117-201657.dump` (fuera del repositorio, en la carpeta que ya usa `infra/bootstrap/backup-v2-database.ps1`) |
| Formato | custom (`pg_dump -Fc`), PostgreSQL 16.11 |
| Tamaño | 114 008 bytes |
| SHA-256 | `5dd16bf400a77fbbb8580479306a50ecb6f3ba26eec8de6e0c80ba2450fad9c9` |
| Verificación | `pg_restore --list`: 225 entradas; **33 de 33 tablas esperadas** (las 32 del modelo + `__EFMigrationsHistory`), **33 con datos**, ninguna faltante ni inesperada |
| Archivos junto al respaldo | `.dump.toc.txt` (índice de `pg_restore --list`) y `.report.txt` (este resumen) |

El respaldo contiene datos de usuarios y hashes de contraseñas: no se copia al repositorio ni a ningún otro lado.

#### Procedimiento de carga del catálogo de demo en `turisclick_staging_o19` (no ejecutado; espera tu aprobación)

**Con qué código:** con el código **actual** de `master`, antes de implementar la fase 1. Así el catálogo entra
con sus NIT `NIT-DEMO-…` y la base reproduce el dato inválido que la fase 1 tiene que detectar.

**Pasos:**

1. **Identidad** (sólo lectura): base exactamente `turisclick_staging_o19`, servidor local; si no, se aborta.
2. **API local apuntada a esa base**, con el patrón de `docs/evidencia/oleada19/run_staging.py`, todo por variables
   de entorno de ese proceso (no se modifican user-secrets ni archivos):
   - `ConnectionStrings__DefaultConnection`: la cadena local de los user-secrets con `Database=turisclick_staging_o19`;
   - `Database__ExpectedName=turisclick_staging_o19` (el chequeo de salud falla si la API ve otra base);
   - `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://localhost:5299`;
   - `Seed__Enabled=false`: **el seeder no corre**, para que no escriba nada por su cuenta (cuenta admin, destinos,
     limpieza de destinos de prueba);
   - `Jwt__Key`: una clave aleatoria generada en memoria para esta corrida; los tokens no sirven en ningún otro
     entorno.
3. **Comprobar la API:** `GET http://localhost:5299/api/health` sano (base esperada y 0 migraciones pendientes).
4. **Cargar** con `node tools/demo-catalog/load-catalog.mjs` desde un envoltorio que:
   - fija `TURISCLICK_API=http://localhost:5299` y **aborta si no empieza con `http://localhost`** (el loader, si
     falta la variable, apunta a Azure V2 por defecto: ese es el riesgo principal y se bloquea en el envoltorio);
   - **no usa `run-catalog.ps1`**, porque éste lee Key Vault (Azure) y el almacén DPAPI de contraseñas de demo;
   - `ADMIN_PASSWORD`: la del administrador local `admin@turisclick.dev`, tomada de los user-secrets
     (`Seed:AdminPassword`) igual que en la Oleada 19, sólo en memoria y sin mostrarla (Q-1c);
   - `PROVIDER_PASSWORD`, `CATALOG_PROVIDER_PASSWORD` y la del turista demo: **generadas al azar en memoria** para
     esta corrida; no se guardan. Las cuentas quedan con contraseñas que nadie conoce (para un diagnóstico no hace
     falta entrar con ellas; si hiciera falta, el administrador regenera la credencial).
   - Si el administrador no puede iniciar sesión, o falta alguna de las categorías que exige el catálogo
     (`Aventura`, `Cultura`, `Gastronomía`, `Historia`, `Naturaleza`, `Relax y bienestar`), el loader **se detiene
     antes de escribir**.
5. **Detener la API** y verificar en sólo lectura: identidad, 15 migraciones y conteos.

**Qué se crea o modifica** (cifras del catálogo; el loader imprime los totales reales):

| Registro | Cantidad | Detalle |
|---|---|---|
| Empresas + cuentas de operador | hasta 8 + 8 | Aprobadas al crearse; NIT `NIT-DEMO-…`, correos `@turisclick.dev`, sin teléfono |
| Destinos (ciudades) | hasta 5 nuevas | Copacabana, Coroico, Rurrenabaque, Samaipata, Torotoro, bajo su departamento |
| Imágenes de destinos | hasta 41 actualizaciones | `PUT` sobre ciudades existentes: cambia su URL de imagen |
| Experiencias | 90 | Con imágenes, categorías y publicación |
| Paquetes | 13 | Con ítems, imágenes y publicación |
| Fechas de disponibilidad | del orden de mil | Semanales hasta el 31/12/2026 y quincenales hasta el 31/03/2027 (experiencias); cada 21 días (paquetes) |
| Cuenta de turista demo | 1 | `turista.demo@turisclick.dev` |
| Cuentas existentes | — | Si en la base ya hay una cuenta del catálogo (p. ej. `proveedor.demo@turisclick.dev`), el loader **regenera su credencial** y renombra/actualiza su empresa al contenido del catálogo |

No se crean categorías, reservas ni pagos. Nada viene de Azure ni de producción: el contenido sale de
`tools/demo-catalog/catalog.mjs`, versionado en el repositorio.

**Cómo se evitan duplicados:** el loader es idempotente por diseño: busca empresas por NIT (y luego por nombre),
destinos por nombre y tipo, experiencias y paquetes por título dentro de cada operador, y fechas por día; actualiza
con `PUT` lo que ya existe y sólo agrega lo que falta. La API además rechaza destinos repetidos (409) y fechas
repetidas (índices únicos). Volver a correrlo no duplica; si algo sale mal a mitad de camino, se restaura el respaldo.

#### Carga del catálogo de demo — ejecutada el 17/11/2026 (20:21–20:22)

Autorizada por vos exclusivamente en `turisclick_staging_o19`. Resultado: **completa, sin errores** (código de
salida 0 del loader).

**Verificaciones previas (todas aprobadas):**

| Verificación | Resultado |
|---|---|
| Conexión | Host de la cadena `localhost`; sesión en `turisclick_staging_o19`, servidor `::1`, puerto 5432, sólo lectura, 15 migraciones |
| Respaldo | Existe; SHA-256 recalculado `5dd16bf4…2450fad9c9` **coincide** con el registrado; `pg_restore --list` sin errores y con 33 tablas con datos |
| URL de la API | Forzada a `http://localhost:5299`; el envoltorio abortaba ante cualquier URL no local |
| API local | Compilada del código actual sin cambios; `GET /api/health?expect=<huella>` ⇒ `200 HEALTHY`, `schemaMatches: true` (conectada exactamente a `turisclick_staging_o19` con las 15 migraciones del repositorio) |
| Escrituras automáticas | Seeder **desactivado** (`Seed__Enabled=false`); también desactivados los tres servicios de fondo que escriben en la base (expiración de reservas, resolución de cancelaciones y reconciliación de vuelos). Proveedor de vuelos `Fake`, asistente `Deterministic` |

**Qué se creó o modificó** (conteos antes y después, en sesión de sólo lectura con la API ya detenida):

| Tabla | Antes | Después | Diferencia |
|---|---|---|---|
| `companies` | 2 | 10 | +8 |
| `users` | 5 | 14 | +9 (8 operadores + 1 turista) |
| `categories` | 6 | 6 | 0 |
| `destinations` | 52 | 57 | +5 (Copacabana, Coroico, Rurrenabaque, Samaipata, Torotoro) |
| `experiences` | 0 | 100 | +100 |
| `experience_images` | 0 | 282 | +282 |
| `experience_availabilities` | 0 | 2000 | +2000 |
| `packages` | 2 | 15 | +13 |
| `package_items` | 2 | 58 | +56 |
| `package_images` | 0 | 39 | +39 |
| `package_availabilities` | 3 | 111 | +108 |
| `reservations` | 3 | 3 | 0 |

- Modificaciones sobre registros existentes: **43 destinos** recibieron su imagen (`UPDATE destinations`, sólo la
  URL); las 100 experiencias y 13 paquetes nuevos se publicaron. El registro SQL de la API no muestra ninguna
  escritura en reservas, pagos ni cancelaciones; las únicas otras escrituras son sesiones (`refresh_tokens`) y el
  cambio de contraseña inicial de las 8 cuentas de operador nuevas.
- El catálogo tiene hoy **100 experiencias** (el README de `tools/demo-catalog` todavía dice 90).
- Acciones de legado del loader: "Sucre colonial: centro histórico" y `qa.turista@turisclick.dev` **no existen**
  en esta base, así que no se despublicó ni suspendió nada.

**Credenciales:** **ninguna credencial regenerada** (el loader no encontró cuentas preexistentes del catálogo).
Cuentas **nuevas**, creadas con contraseñas aleatorias que sólo existieron en memoria durante la corrida y no se
guardaron (nadie las conoce; si hiciera falta entrar, el administrador regenera la credencial):
`proveedor.demo@turisclick.dev`, `proveedor.lagoyungas@turisclick.dev`, `proveedor.surandino@turisclick.dev`,
`proveedor.llajta@turisclick.dev`, `proveedor.charcas@turisclick.dev`, `proveedor.chapaca@turisclick.dev`,
`proveedor.curichi@turisclick.dev`, `proveedor.jichi@turisclick.dev` y `turista.demo@turisclick.dev`. La contraseña
del administrador local se usó sólo en memoria.

**Contraseñas en registros:** se buscaron la del administrador, las tres generadas, la de la base y la clave JWT en
la salida de la API, en los archivos nuevos de `src/TurisClick.Api/logs/` (ignorado por git) y en la salida del
loader: **no aparecen en ninguno**. Las salidas quedaron fuera del repositorio, en la carpeta temporal de esta sesión
(`…\scratchpad\carga-demo\`).

**Cierre:** API detenida y puerto 5299 libre; nueva verificación de identidad y de las 15 migraciones al terminar.
**No se aplicó la `0016`**, no se tocó ninguna otra base, ni Azure, ni producción; no hubo commit.

Con esto `turisclick_staging_o19` reproduce el caso del tutor: **8 empresas con NIT `NIT-DEMO-…`** más las 2 que ya
tenía, listas para el diagnóstico de §13.3 cuando lo autorices.

#### Restauración del respaldo (no ejecutada)

Para volver `turisclick_staging_o19` exactamente al estado del 17/11/2026 20:16:

1. Detener cualquier API local conectada a esa base.
2. Verificar la identidad (base exacta, servidor local) y el SHA-256 del archivo.
3. Restaurar **sobre esa misma base**, todo o nada:

   ```bash
   pg_restore --clean --if-exists --single-transaction --exit-on-error -h localhost -p 5432 -U <usuario> -d turisclick_staging_o19 "C:\Users\Juan Pablo\turisclick-backups\validaciones-fase1\turisclick_staging_o19-antes-de-catalogo-demo-20261117-201657.dump"
   ```

   `--clean --if-exists` elimina y recrea los objetos del respaldo (incluye lo que hubiera agregado la `0016`, porque
   sus índices y FK viven en esas tablas); `--single-transaction` hace que, si algo falla, la base quede como
   estaba.
4. Verificar en sólo lectura: 15 migraciones y **2 empresas, 6 categorías, 5 usuarios**.

No crea ni borra bases, y no toca ninguna otra base local.

**Consecuencia importante:** el repositorio no define un staging en Azure. Una base de staging local sólo contiene
lo que se haya cargado en ella (p. ej. el catálogo de demo de la Oleada 19), así que **no muestra el estado de los
datos que ve el tutor en Azure**. Lo que se sabe de esos datos sale del repositorio: los 8 NIT `NIT-DEMO-…` del
catálogo de demo y el `NIT-QA-E2E` de la prueba de punta a punta (Q-2).

### 13.2 Verificación de identidad antes de cualquier consulta

Ninguna consulta se ejecuta sin tu confirmación explícita de la base (Q-1). Una vez confirmada:

1. Conexión con una cadena que **vos** indicás (o tus user-secrets), sin guardarla en archivos del repositorio.
2. Sesión de sólo lectura: `PGOPTIONS='-c default_transaction_read_only=on'`, además de `BEGIN READ ONLY`.
3. Primera consulta, y **nada más** hasta comparar su resultado:

```sql
SELECT current_database()      AS base,
       inet_server_addr()      AS host,      -- NULL o 127.0.0.1/::1 si es local
       inet_server_port()      AS puerto,
       current_user            AS usuario,
       current_setting('lc_ctype')               AS lc_ctype,
       current_setting('server_version')         AS version,
       current_setting('transaction_read_only')  AS solo_lectura;
```

4. Se continúa **sólo si**: `base` es exactamente la que confirmaste; no está en la lista prohibida
   (`turisclick_db`, `turisclick_db_v2`, `turisclick_v2_dev`, `turisclick_v2_test`); `host` es local (o el que
   confirmaste explícitamente); `solo_lectura = on`. Si algo no coincide, se corta y se te informa.
5. Se ejecutan las consultas de §13.3 y se te entrega el resultado en un archivo de la carpeta de notas, sin datos
   personales innecesarios (ids, nombres de empresa y campo afectado; los correos sólo con su dominio).

### 13.3 Consultas de diagnóstico (texto; no se ejecutaron)

```sql
-- 1. NIT que no cumplen R-03 (tras quitar separadores)
SELECT id, name, legal_document FROM companies
WHERE regexp_replace(legal_document, '[\s.\-]', '', 'g') !~ '^[0-9]{5,12}$'
   OR regexp_replace(legal_document, '[\s.\-]', '', 'g') ~ '^(.)\1*$';

-- 2. NIT que colisionarían al normalizarlos
SELECT regexp_replace(legal_document, '[\s.\-]', '', 'g') AS nit, count(*), array_agg(id) FROM companies
GROUP BY 1 HAVING count(*) > 1;

-- 3. Empresas con la misma K1 (la creación del índice fallaría)
SELECT translate(lower(regexp_replace(btrim(name), '\s+', ' ', 'g')),
                 'ÁÀÂÄÃÉÈÊËÍÌÎÏÓÒÔÖÕÚÙÛÜáàâäãéèêëíìîïóòôöõúùûü',
                 'aaaaaeeeeiiiiooooouuuuaaaaaeeeeiiiiooooouuuu') AS k1,
       count(*), array_agg(name), array_agg(id)
FROM companies GROUP BY 1 HAVING count(*) > 1;

-- 4. Categorías con la misma K1 (misma expresión)
SELECT translate(lower(regexp_replace(btrim(name), '\s+', ' ', 'g')),
                 'ÁÀÂÄÃÉÈÊËÍÌÎÏÓÒÔÖÕÚÙÛÜáàâäãéèêëíìîïóòôöõúùûü',
                 'aaaaaeeeeiiiiooooouuuuaaaaaeeeeiiiiooooouuuu') AS k1,
       count(*), array_agg(name), array_agg(id)
FROM categories GROUP BY 1 HAVING count(*) > 1;

-- 5. Nombres de empresa fuera de R-01 (largo o menos de dos letras)
SELECT id, name, char_length(name) FROM companies
WHERE char_length(btrim(name)) NOT BETWEEN 3 AND 100 OR name !~ '[[:alpha:]].*[[:alpha:]]';

-- 6. Categorías fuera de R-07
SELECT id, name FROM categories
WHERE char_length(btrim(name)) NOT BETWEEN 3 AND 40 OR name !~ '[[:alpha:]].*[[:alpha:]]';

-- 7. Teléfonos que no son de Bolivia ni internacionales válidos
SELECT id, contact_phone FROM companies
WHERE contact_phone IS NOT NULL
  AND regexp_replace(contact_phone, '[\s.()\-]', '', 'g') !~ '^(\+591|591)?([234][2-689][0-9]{6}|[567][0-9]{7}|800[0-9]{6})$'
  AND regexp_replace(contact_phone, '[\s.()\-]', '', 'g') !~ '^\+[1-9][0-9]{6,14}$';

-- 8. Correos: largo, espacios o caracteres no ASCII
SELECT id, split_part(email, '@', 2) AS dominio FROM users
WHERE char_length(email) > 254 OR email ~ '\s' OR email ~ '[^\x01-\x7F]';
SELECT id, split_part(contact_email, '@', 2) AS dominio FROM companies
WHERE char_length(contact_email) > 254 OR contact_email ~ '\s' OR contact_email ~ '[^\x01-\x7F]';

-- 9. Nombres de personas fuera de R-06
SELECT id FROM users
WHERE first_name !~ '^[[:alpha:]][[:alpha:] ''’.\-]*$' OR last_name !~ '^[[:alpha:]][[:alpha:] ''’.\-]*$'
   OR char_length(first_name) NOT BETWEEN 2 AND 100 OR char_length(last_name) NOT BETWEEN 2 AND 100;

-- 10. Uso de categorías (anticipa los 409 de R-09)
SELECT c.id, c.name,
       (SELECT count(*) FROM experience_categories e WHERE e.category_id = c.id) AS experiencias,
       (SELECT count(*) FROM package_categories p WHERE p.category_id = c.id) AS paquetes,
       (SELECT count(*) FROM tourist_preference_categories t WHERE t.category_id = c.id) AS turistas
FROM categories c ORDER BY c.name;
```

---

## 14. Corrección segura de datos existentes

Según C-8 no se crea un endpoint para editar el NIT. Procedimiento:

1. **Identificar:** consultas de §13.3 (o el mismo diagnóstico contra otra base que autorices) ⇒ listado.
2. **Registrar:** por cada registro, un renglón en `docs/correcciones-datos/AAAA-MM-DD-<tema>.md` con id, campo,
   valor actual, valor propuesto, motivo y fuente del dato correcto (p. ej. el NIT que figura en el documento de la
   empresa). Para datos de demo, el valor propuesto es un NIT numérico ficticio y se dice expresamente.
3. **Autorizar:** vos aprobás ese registro por escrito antes de cualquier cambio (y, si es una base de Azure, también
   quién lo ejecuta).
4. **Respaldar:** exportar las filas afectadas (`SELECT` a CSV) y, si es Azure, usar el script existente
   `infra/bootstrap/backup-v2-database.ps1` antes de tocar nada.
5. **Ejecutar** sólo lo autorizado, en una transacción que verifica el valor viejo y devuelve evidencia:

   ```sql
   BEGIN;
   UPDATE companies SET legal_document = '<nuevo>'
   WHERE id = '<id>' AND legal_document = '<valor actual>'
   RETURNING id, legal_document;
   -- se confirma (COMMIT) sólo si devolvió exactamente una fila; si no, ROLLBACK
   COMMIT;
   ```

6. **Dejar constancia:** pegar el resultado en el registro del paso 2 (fecha, quien ejecutó, filas afectadas).
7. **Cerrar:** cuando las consultas de §13.3 den cero filas, recién entonces se proponen los `CHECK` de formato.

Los campos que la aplicación sí permite editar (nombre, descripción, correo y teléfono de empresas aprobadas;
categorías) se corrigen desde la propia aplicación, que ya exigirá las reglas nuevas.

---

## 15. Riesgos

| # | Riesgo | Probabilidad / impacto | Mitigación |
|---|---|---|---|
| RS-1 | Duplicados normalizados existentes impiden aplicar la migración | Media / alto | Fase 0 + verificación previa en la migración + §14 |
| RS-2 | La base desplegada (Azure V2) tiene datos que staging no refleja | Alta / medio | Aplicar la migración en Azure queda fuera de la fase 1; antes, un diagnóstico autorizado específico |
| RS-3 | Clientes que envían NIT con letras dejan de funcionar | Seguro / bajo | Se actualizan tests, `tools/demo-catalog` y la colección de Postman en la misma entrega |
| RS-4 | Regla demasiado estricta rechaza un dato legítimo (p. ej. un nombre con un signo no previsto) | Media / medio | Listas de caracteres amplias, archivo común de casos con ejemplos reales, y el ajuste es cambiar una constante en dos lugares cubiertos por el test de contrato |
| RS-5 | Diferencia entre K1 de C#, TypeScript y PostgreSQL | Baja / medio | Archivo común de casos + prueba de persistencia que compara con la base |
| RS-6 | El cambio del formato de error rompe pantallas que leen el error crudo | Baja / bajo | Se mantiene la firma de `getErrorMessage`; la app móvil sólo lee valores de `errors`; se corre su suite |
| RS-7 | La advertencia de nombres parecidos da falsos positivos | Media / bajo | No bloquea; sólo informa |
| RS-8 | `lc_ctype` distinto en otra base | Baja / bajo | Consultado en la fase 0; `translate` explícito para tildes |
| RS-9 | Conflicto en `frontend/package-lock.json`: el checkout principal tiene cambios sin commit de la tarea de Expo | Alta / medio | Coordinar: agregar el workspace después de que esa tarea haga commit, o regenerar el lock sobre su versión |
| RS-10 | Tipos de API generados desactualizados | Media / bajo | Regenerar `schema.ts` con la API local al final |

---

## 16. Lo que necesito de vos antes de empezar

| # | Pregunta |
|---|---|
| **Q-1** | Verificado el 17/11/2026: `turisclick_staging_o19` existe en el PostgreSQL local; `turisclick_db_staging` no existe (§13.1). ¿Opción A (verificar en sólo lectura si `turisclick_staging_o19` sirve) u opción B (crear `turisclick_db_staging` con el procedimiento de §13.1)? |
| **Q-1b** | `turisclick_staging_o19` tiene el esquema al día (15/15) pero sólo 2 empresas, 6 categorías y 5 usuarios. ¿(1) Usarla tal cual para la fase 0, sabiendo que el diagnóstico será limitado; (2) usarla sólo para ensayar la migración `0016` y cargarle además el catálogo de demo (escritura en esa base local, a autorizar); o (3) crear `turisclick_db_staging` con el procedimiento de §13.1 y cargarle el catálogo de demo? Recomendado: (2), porque no crea bases nuevas y deja datos representativos de los NIT de demo. **Respondida: opción 2.** Respaldo hecho y verificado (§13.1); la carga espera tu aprobación. |
| **Q-1c** | Para la carga: ¿aprobás el procedimiento de §13.1, incluido usar la contraseña del administrador local (`Seed:AdminPassword` de los user-secrets, sólo en memoria, como en la Oleada 19) y contraseñas aleatorias no guardadas para las cuentas de demo de staging? Si preferís no usar esa contraseña, la alternativa es que inicies vos la sesión o me indiques otra forma. **Respondida: aprobado; carga ejecutada el 17/11/2026 (§13.1).** |
| **Q-2** | Si staging es local, no va a reflejar los datos de Azure V2. ¿Alcanza con diagnosticar staging y con lo que se sabe del repositorio (NIT `NIT-DEMO-…`), o preferís una autorización específica para correr el mismo diagnóstico de sólo lectura en otra base? (Hoy C-10 lo prohíbe y no lo voy a hacer sin tu orden.) |
| **Q-3** | ¿Incluimos en la migración el índice único sobre el NIT normalizado (sólo si la fase 0 no encuentra colisiones)? Recomendado: sí. |
| **Q-4** | ¿Aprobás el endpoint nuevo `GET /api/admin/companies/name-check` (sólo administradores, sólo lectura) para la advertencia de nombres parecidos? |
| **Q-5** | ¿Aprobás pasar las FK de categorías a `RESTRICT` en la base, además de la verificación en el servicio? Recomendado: sí (cubre la concurrencia). |
| **Q-6** | ¿Autorizás comenzar la implementación de la fase 1 según §10, con commit sólo después de tu revisión? |

---

## 17. Anexos: relevamiento

### 17.1 Cómo valida hoy cada capa

- **Backend:** DataAnnotations en DTOs e `IValidatableObject`; 400 automático de `[ApiController]` sin
  `InvalidModelStateResponseFactory`; reglas de negocio en servicios con mensajes en español; único validador propio
  `Iso4217CurrencyAttribute`; recorte y minúsculas **después** de validar; contraseñas con `PasswordHasher<User>`
  (PBKDF2).
- **Base:** `varchar(n)`, `NOT NULL`, índices únicos exactos (`uq_users_email`, `uq_companies_legal_document`,
  `uq_categories_name`, destinos por nombre+padre+tipo), algunos `CHECK` (precio, moneda, cupos, IATA, rol); ningún
  `CHECK` de formato; sin `unaccent`.
- **Backoffice:** `react-hook-form` + `zod` 4 en la mayoría de los formularios; límites copiados a mano; banner único
  vía `getErrorMessage`; antecedente útil `__tests__/enumValuesMatchBackend.test.ts`.
- **App móvil:** `react-hook-form` + `zod` en login y registro; `toApiError` maduro pero deja pasar mensajes por
  defecto en inglés.

### 17.2 Inventario de formularios del backoffice

Rutas relativas a `frontend/apps/backoffice/src/`.

| # | Formulario | Rol | Componente | Endpoint | DTO | Fase |
|---|---|---|---|---|---|---|
| 1 | Alta de operador (empresa + cuenta) | ADMIN | `modules/companies/NewProviderAccountPage.tsx` | `POST /api/admin/provider-accounts` | `CreateProviderAccountRequest` | 1 |
| 2 | Alta de administrador | ADMIN | `modules/administrators/NewAdminAccountPage.tsx` | `POST /api/admin/admin-accounts` | `CreateAdminAccountRequest` | 1 |
| 3 | Categorías | ADMIN | `modules/categories/CategoriesPage.tsx` | `POST`/`PUT`/`DELETE /api/admin/categories` | `Create/UpdateCategoryRequest` | 1 |
| 4 | Mi empresa | PROVIDER | `modules/my-company/MyCompanyPage.tsx` | `PUT /api/companies/me` | `UpdateCompanyRequest` | 1 |
| 5 | Rechazo de empresa | ADMIN | `modules/companies/RejectDialog.tsx` | `POST /api/admin/companies/{id}/reject` | `RejectCompanyRequest` | 1 |
| 6 | Suspender / reactivar empresa | ADMIN | `modules/companies/SuspensionDialog.tsx` | `…/suspend`, `…/reactivate` | — | — |
| 7 | Venta de vuelos | ADMIN | `modules/companies/FlightSellerCard.tsx` | `PUT /api/admin/companies/{id}/flight-seller` | `FlightSellerAuthorizationRequest` | 1 |
| 8 | Credenciales de la empresa | ADMIN | `modules/companies/CompanyAccountsCard.tsx` | `…/reset-password` | — | — |
| 9 | Usuarios | ADMIN | `modules/users/UsersPage.tsx` | `…/suspend`, `…/activate` | — | — |
| 10–12 | Destinos (alta, edición, imagen) | ADMIN | `modules/destinations/*` | `/api/admin/destinations…` | `Create/UpdateDestinationRequest` | 2 |
| 13–14 | Experiencias y disponibilidad | PROVIDER | `modules/experiences/*`, `modules/availability*/*` | `/api/experiences…` | DTOs de experiencias | 2 |
| 15–17 | Paquetes, vuelo del paquete, disponibilidad | PROVIDER | `modules/packages/*`, `modules/package-availability/*` | `/api/packages…` | DTOs de paquetes | 2 |
| 18–19 | Vuelos de operador y salidas | PROVIDER | `modules/provider-flights/*` | `/api/companies/me/flights…` | `ProviderFlight*Request` | 2 |
| 20–21 | Cancelación y emisión | PROVIDER | `modules/provider-reservations/*` | `/api/companies/me/reservations…` | `CancelReservationItemRequest`, `RecordIssuanceRequest`, `IssuanceFailureRequest` | 2 |
| 22 | Sanciones y pagos | ADMIN | `modules/platform/*` | `/api/admin/…` | — | — |
| 23 | Inicio de sesión | todos | `auth/LoginPage.tsx` | `POST /api/auth/login` | `LoginRequest` | 1 |
| 24 | Cambio de contraseña | todos | `auth/ChangePasswordPage.tsx` | `POST /api/auth/change-password` | `ChangePasswordRequest` | 1 |

### 17.3 App móvil de turistas (fase 3)

Registro (`app/(auth)/register.tsx`), login, onboarding (`app/onboarding.tsx`), reservas (`app/book/*`), pasajeros
al pagar (`src/features/flights/TravelerForm.tsx` en `Checkout.tsx`), pasajeros de vuelo de operador
(`src/features/flights/booking/model.ts`; no encontré una pantalla que use `validatePassenger`), búsqueda de vuelos,
cancelación y asistente. Huecos: nombres con dígitos, contraseña de 8, máximos del servidor no aplicados en la app y
mensajes por defecto en inglés.

### 17.4 Fuentes

- SIN — [SIAT Info: Generación del NIT](https://siatinfo.impuestos.gob.bo/index.php/requisitos-para-la-inscripcion/conceptos-generales/generacion-del-nit) ✔
- SIN — [SIAT Info: Finalización de la inscripción](https://siatinfo.impuestos.gob.bo/index.php/requisitos-para-la-inscripcion/conceptos-generales/finalizacion) ✔
- SIN — [SIAT Info: Factura de Compra y Venta](https://siatinfo.impuestos.gob.bo/index.php/facturacion-en-linea/archivos-xml-xsd-de-facturas-electronicas/factura-de-compra-y-venta) ✔
- SIN — [Especificación Técnica del Código de Control v7.0](https://www.impuestos.gob.bo/wp-content/uploads/2025/10/especificaciones-tecnicas-codigo-de-control-ver-7.pdf) ✔
- SIN — [RND 10-0032-04](https://www.impuestos.gob.bo/wp-content/uploads/2025/10/RND10-0032-04.pdf) ⚠ (no legible)
- Ministerio de Obras Públicas, Servicios y Vivienda — [RM 339/2021, Plan Técnico Fundamental de Numeración](https://www.oopp.gob.bo/wp-content/uploads/2022/03/2021-RM339-Plan-Tecnico-Fundamental-de-Numeracion.pdf) ✔
- IETF — [RFC 5321 §4.5.3.1](https://www.rfc-editor.org/rfc/rfc5321#section-4.5.3.1) ✔ · [errata 1690 de RFC 3696](https://errata.rfc-editor.org/eid1690) ✔
- ISO 4217 (unidades menores) — sólo fuentes secundarias, p. ej. [moneyconvert.net](https://moneyconvert.net/currency-codes/) ⚠

---

## 18. Informe de implementación de la fase 1 (17/11/2026, sin commit)

**Diagnóstico de staging (sólo lectura, `turisclick_staging_o19`, local):** 10 NIT inválidos (8 `NIT-DEMO-…` y 2 de QA);
ningún NIT ni nombre normalizado duplicado; nada más fuera de las reglas. Todas las funciones de las expresiones de índice
son `IMMUTABLE`. Categorías: las 6 están en uso.

**Migración `0016_Fase1_Validaciones`:** verificación previa de duplicados (aborta en español sin aplicar nada), FK de
categorías → `RESTRICT` (experiencias, paquetes, preferencias; `ai_conversation_categories` queda en CASCADE — decisión
pendiente), índices únicos `uq_companies_name_key`, `uq_categories_name_key`, `uq_companies_legal_document_normalized`.
Ensayada **sólo** en `turisclick_fase1_ensayo_tmp` (restaurada de un `pg_dump` de staging, ya borrada): Up, Down,
aborto por duplicado y re-Up correctos; datos intactos. **No se aplicó** en staging, Azure ni ninguna otra base.

**Backend:** `Shared/Validation/` (límites, normalización, reglas, atributos, `NameKeySql`, `ValidationProblemFactory`),
`GlobalExceptionHandler` (23505 ⇒ 409; 23514/22001/22003 ⇒ 400; errores por campo), DTOs y servicios de alta de
operador, administradores, Mi empresa, rechazo, venta de vuelos, login, cambio de contraseña y categorías (unicidad
normalizada con captura de concurrencia, borrado bloqueado en uso), endpoint `GET /api/admin/companies/name-check`.

**Frontend:** paquete `@turisclick/validation` (reglas, esquemas zod, traductor de errores, `applyServerErrors`,
`UserMessageError`), `FormField`/`fieldAria`, formularios de la fase 1, `z.config(z.locales.es())`, `checkCompanyName`
en api-client. La app móvil no se modificó.

**Datos de prueba:** helpers de tests con NIT numéricos, `catalog.mjs`/`validate-e2e.mjs`/README y colección de Postman
con NIT numéricos.

**Pruebas:** backend unitarias 431/431 y suite de integración completa (antes de las pruebas nuevas) 470/472 → corregidas;
pruebas nuevas de la fase 1 + categorías 63/63, contra la base desechable `turisclick_fase1_tests_tmp`. Backoffice:
typecheck OK, 388/388, lint sin advertencias nuevas en los archivos tocados. App móvil: 528/528, typecheck OK.
Corrida de la solución completa (17/11/2026): 957/958; la única falla (`CompaniesEndpointsTests.UpdateMyCompany_AfterApproval_Succeeds`)
usaba un nombre de empresa fijo que ya existía en la base de tests de una corrida anterior —ahora el nombre es único— y se
corrigió con un nombre distinto por corrida. **Re-corrida de confirmación: 958 aprobadas, 0 fallidas, 0 omitidas**, contra la
base desechable local `turisclick_fase1_tests_rerun_tmp` (servidor `::1`), creada para la corrida y **borrada al terminar**.

**Decisión sobre `ai_conversation_categories` (17/11/2026):** se mantiene `ON DELETE CASCADE`. Las categorías asociadas a
conversaciones con el asistente son contexto histórico: **las conversaciones no bloquean la eliminación de una categoría**;
al eliminarla se quita de esas conversaciones. El bloqueo de D-11 rige sólo para experiencias, paquetes y preferencias de
turistas (`RESTRICT`). Documentado también en `Modules/Ai/Persistence/AiConversationConfiguration.cs`; el comportamiento no
cambió (la migración 0016 no toca esa FK).

**`schema.ts` regenerado (17/11/2026):** con la API local en el puerto 5288 contra la base desechable
`turisclick_fase1_schema_tmp` (16 migraciones, incluida la 0016; seeder y servicios de fondo apagados), borrada al terminar.
El diff es sólo de la fase 1: el endpoint `name-check` y sus tipos (`CompanyNameCheckResponse`, `CompanyNameMatchResponse`),
y la desaparición de la anotación `Format: email` en los DTO que dejaron de usar `[EmailAddress]`; no había cambios ajenos
pendientes en `master`. La primera regeneración mostró que, al quitar `[Required]`, Swagger marcaba como opcionales los
campos obligatorios: se agregó `[Required(AllowEmptyStrings = true)]` (sólo contrato; el mensaje en español lo sigue dando
la regla propia) y se volvió a generar. `checkCompanyName` usa ahora los tipos generados. Verificación posterior: backend 958/958 (base
desechable `turisclick_fase1_tests_dto_tmp`, borrada), backoffice typecheck OK y 388/388, app móvil typecheck OK y 528/528.

**Scripts SQL idempotentes de la 0016 para Azure V2 (17/11/2026, sin commit):** `infra/migrations/0016_Fase1_Validaciones.up.sql`
y `.down.sql`, generados con `dotnet ef migrations script --idempotent` y probados con
`psql --single-transaction -v ON_ERROR_STOP=1 -f` sobre la base desechable `turisclick_fase1_script0016_tmp` (restaurada de
un `pg_dump` de `turisclick_staging_o19` con el catálogo; 15 migraciones; borrada al terminar):

- *up*: aplica la verificación previa (el `DO $$` anidado dentro del `DO $EF$` funciona), las 3 FK en `RESTRICT` y los
  3 índices, y registra la migración; los índices rechazan mayúsculas, tildes y espacios repetidos y respetan la ñ. Datos
  intactos (10 empresas, 6 categorías, 163 asignaciones). Aplicado dos veces: idempotente.
- *down*: vuelve a 15 migraciones, sin índices y con las FK en `CASCADE`; idempotente; el *up* se puede reaplicar.
- Aborto: con dos pares de categorías duplicadas el *up* termina con código 3 y el mensaje en español, sin aplicar nada.
- **Codificación:** el script generado, ejecutado con codificación de cliente LATIN1, se aplicaba sin error pero con
  índices incorrectos (dejaba pasar "Aventúra" frente a "Aventura"); con WIN1252 fallaba sin aplicar nada. Los archivos de
  `infra/migrations` llevan, sin BOM, `SET client_encoding = 'UTF8';` como primera sentencia; con eso se probaron con
  LATIN1, WIN1252 y UTF8 (*up*, *down*, idempotencia y aborto) y el resultado es correcto en los tres casos. La migración
  de EF no cambió.
- `psql --single-transaction` muestra dos advertencias inofensivas porque el script trae su propio
  `START TRANSACTION`/`COMMIT`.

**Pendiente / a decidir:** (las bases desechables `turisclick_fase1_ensayo_tmp`, `turisclick_fase1_tests_tmp` y
`turisclick_fase1_tests_rerun_tmp` ya están borradas); `ai_conversation_categories` resuelto (se mantiene CASCADE, ver arriba);
`frontend/package-lock.json` difiere de master por el workspace nuevo (RS-9); los 10 NIT inválidos de staging no se corrigen por ahora (decisión del 17/11/2026); cuando se autorice, seguir el procedimiento
según §14; `schema.ts` ya regenerado (ver arriba).
