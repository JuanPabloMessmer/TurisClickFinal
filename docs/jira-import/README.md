# Importación del backlog de TurisClick a Jira

Archivos del seguimiento Scrum de TurisClick en Jira.

Generados el 28/10/2026 a partir del repositorio: `docs/use-cases.md`, el historial de
`origin/master` y de `legacy/v1`, y la documentación técnica de `docs/`.

> ## ✅ Estado: importado
>
> **28/10/2026** — importadas **217 incidencias** (17 épicas + 200 ítems), claves
> **`TC-1` a `TC-217`**. Una sola ejecución.
> **09/11/2026** — creados los **18 sprints** y asignados los **187 ítems**.
>
> El detalle está en [`08-informe-final.md`](08-informe-final.md) y la correspondencia
> sprint ↔ claves en [`07-sprints-y-claves.md`](07-sprints-y-claves.md).

---

## 1. Qué hay acá

| Archivo | Filas | Para qué |
|---|---|---|
| **`00-turisclick-completo.csv`** | **217** | **El que se importa.** 17 épicas + 200 ítems, con los vínculos resueltos |
| `01-epicas.csv` | 17 | Sólo las épicas, para revisión |
| `02-historias.csv` | 94 | Sólo `Story`, para revisión |
| `03-tareas.csv` | 93 | Sólo `Task`, para revisión |
| `04-bugs.csv` | 13 | Sólo `Bug`, para revisión |
| `05-equivalencias-identificadores.csv` | 33 | Equivalencias de identificadores renumerados |

Todos en **UTF-8 sin BOM**, con todos los campos entrecomillados.

### Columnas (19)

`Issue id` · `Issue Type` · `Summary` · `Description` · `Priority` · `Status` · `Sprint` ·
`Parent id` · `Epica (referencia)` · `Labels` … `Labels.9`

---

## 2. Qué contiene el backlog

**200 ítems** repartidos en **17 épicas**, con **92 dependencias** declaradas.

| Prefijo | Qué es | Cant. |
|---|---|---|
| `UC-*` | Casos de uso del catálogo | 74 |
| `EXT-` | Extensiones de casos de uso existentes | 6 |
| `RNF-` | Requisitos no funcionales | 4 |
| `BUG-` | Defectos corregidos | 11 |
| `TEC-` | Tareas técnicas | 31 |
| `INV-` | Investigación preliminar | 24 |
| `V1-` | Prototipo V1 | 8 |
| `FASE-` | Análisis y diseño (FASES 1–4) | 5 |
| `PEND-` | Pendientes de noviembre | 24 |
| `DOC-` | Documentación de tesis | 11 |
| `FUT-` | Mejoras futuras | 2 |

Por tipo de incidencia: **94 Story · 93 Task · 13 Bug**.
Por estado: **163 Done · 37 To Do**.

### El identificador funcional va en el resumen

Cada tarjeta se llama `[UC-T-08] Reservar una experiencia individual`. El identificador
también está en la descripción, en la línea `ID FUNCIONAL:`, porque el plan Free de un
proyecto *team-managed* no admite campos personalizados.

---

## 3. Tres cosas que este backlog NO contiene, a propósito

### 3.1 No hay story points

La columna de estimación **se eliminó por completo**. No existen estimaciones históricas
verificables, y no se inventan. Si en el futuro se quiere estimar trabajo pendiente, se
activa *Estimation* en el proyecto y se cargan puntos sobre los ítems `To Do`.

### 3.2 No hay fechas de creación ni de resolución fabricadas

El CSV **no trae** las columnas `Created`, `Resolved`, `Updated` ni
`Status Category Changed`, aunque el importador de Jira las admite para administradores.

**Consecuencia:** no habrá burndown ni velocity reales. Los gráficos se dibujarán, pero no
reflejan ejecución: las transiciones nunca ocurrieron en Jira.

### 3.3 El seguimiento se consolidó retrospectivamente

**Ninguno de los 163 ítems en `Done` representa un cierre histórico registrado en Jira.**
El proyecto estaba vacío y nunca tuvo tarjetas.

Cada ítem `Done` lo declara de tres formas:

1. La etiqueta **`retro-2026-10-27`**.
2. En su descripción: *"REGISTRO: ítem consolidado retrospectivamente el 27/10/2026. Jira no
   contiene el historial de transiciones de este trabajo."*
3. Una etiqueta de procedencia que dice en qué se apoya:

| Procedencia | Cant. | Significado |
|---|---|---|
| `evidencia-git` / `evidencia-doc` | 139 | Respaldado por un commit o un documento |
| `reconstruido` + `fecha-estimada` | 16 | Sin evidencia verificable |
| `reconstruido` + `evidencia-parcial` | 5 | El resultado es verificable, el proceso no |
| `evidencia-doc` + `fecha-estimada` | 3 | Entregable real, fecha propuesta |

> **Para la tesis**: conviene declarar que el seguimiento Scrum se consolidó
> retrospectivamente el 27/10/2026, y presentar los sprints como estructura de planificación
> y trazabilidad, no como telemetría de ejecución.

---

## 4. Estado de la configuración del proyecto

| Requisito | Estado |
|---|---|
| Clave del proyecto | ✅ **`TC`** *(era `UC`; Jira conserva `UC` como clave anterior)* |
| Tipos de trabajo | ✅ Epic, Story, Task y Bug existen |
| Jerarquía Epic → Story/Task/Bug | ✅ es la nativa |
| Campo **Parent** | ✅ en los cuatro tipos |
| Campo **Labels** | ✅ en los cuatro tipos |
| Campo **Priority** | ✅ agregado a Story, Task y Bug; Epic ya lo tenía |
| Prioridades | ✅ Highest · High · Medium · Low · Lowest |
| Estados | ✅ To Do · In Progress · In Review · Done |
| Vista **Backlog** | ✅ agregada a la navegación |
| **Sprints** | ✅ función activada; **18 sprints creados**, todos en estado *future* |
| Estimation | ⬜ **desactivada a propósito** (ver 3.1) |

---

## 5. Procedimiento de importación

> Ninguno de estos pasos se ejecutó.

### Paso 1 — Activar Sprints

Agregar la vista **Backlog** al proyecto (menú `+` de la barra de vistas →
*Backlog* → *Add to navigation*), y después activar **Sprints** en
*Ajustes del espacio → Funciones*.

Sin esto, la columna `Sprint` del CSV no tiene destino y los 18 sprints no se crean.

### Paso 2 — Importar

*Configuración → Sistema → Importación y exportación → **External system import** → CSV*.

1. Subir **`00-turisclick-completo.csv`**.
2. Proyecto destino: **TurisClick (TC)**.
3. Codificación **UTF-8**, separador **coma**.
4. **Mapeo de columnas**, verificar a mano:
   - `Issue id` → *Issue id*
   - `Parent id` → *Parent id* ← **el crítico**: sin esto no se arman las épicas
   - `Sprint` → *Sprint*
   - Las 10 columnas `Labels*` → *Labels*
   - `Epica (referencia)` → **no mapear**, es auxiliar para lectura humana
5. **Mapeo de valores**: confirmar `To Do` y `Done`, y las cinco prioridades.
6. Importar: se crean **217 incidencias**.

### Paso 3 — Fechar los sprints a mano

**El importador no admite fechas de sprint.** Crea los sprints por nombre, sin fechas y sin
cerrarlos. Hay que editarlos uno por uno en el Backlog.

| Sprint | Inicio | Fin | | Sprint | Inicio | Fin |
|---|---|---|---|---|---|---|
| 1 | mié 01/04 | mar 14/04 | | 10 | mié 05/08 | mar 18/08 |
| 2 | mié 15/04 | mar 28/04 | | 11 | mié 19/08 | mar 01/09 |
| 3 | mié 29/04 | mar 12/05 | | 12 | mié 02/09 | mar 15/09 |
| 4 | mié 13/05 | mar 26/05 | | 13 | mié 16/09 | mar 29/09 |
| 5 | mié 27/05 | mar 09/06 | | 14 | mié 30/09 | mar 13/10 |
| 6 | mié 10/06 | mar 23/06 | | 15 | mié 14/10 | mar 27/10 |
| 7 | mié 24/06 | mar 07/07 | | 16 | mié 28/10 | mar 10/11 |
| 8 | mié 08/07 | mar 21/07 | | 17 | mié 11/11 | mar 24/11 |
| 9 | mié 22/07 | mar 04/08 | | 18 | mié 25/11 | **lun 30/11** |

Cerrar del **1 al 14**. Dejar el **15 activo**. El 16, 17 y 18 quedan futuros.

**Cadencia**: miércoles a martes, 2 semanas. **Única excepción: el Sprint 18 dura 6 días**,
para cerrar en la fecha meta del 30/11/2026.

**El Sprint 7 queda vacío**, con fechas y cerrado. Entre el 18/06 y el 13/07 no hay un solo
commit en `legacy/v1`. **No se interpreta como inactividad**, sólo como ausencia de
evidencia: no se le inventa contenido.

### Paso 4 — Validar

| Chequeo | Esperado |
|---|---|
| Incidencias creadas | 217 |
| Épicas | 17 |
| Ítems sin épica | 0 |
| Story · Task · Bug | 94 · 93 · 13 |
| Done · To Do | 163 · 37 |
| Ítems sin sprint | 13 |
| Sprint 7 | 0 ítems |
| Casos de uso representados | 74 *(buscar `[UC-` en el resumen)* |
| Etiqueta `retro-2026-10-27` | 163 |

---

## 6. Los 13 ítems sin sprint

| Ítem | Motivo |
|---|---|
| `UC-P-03` | Marcado *Backlog* en `docs/use-cases.md`: nunca se implementó |
| `DOC-01` a `DOC-10` | Sin fecha de entrega del documento ni de defensa confirmadas |
| `FUT-01`, `FUT-02` | Fuera del cierre de noviembre por decisión explícita |

---

## 7. Identificadores renumerados

`05-equivalencias-identificadores.csv` registra los cambios, para no perder referencias:

- El antiguo **`TEC-02`** (*retirar el paquete OpenApi sin uso*) quedó **absorbido en
  `TEC-01`**: era una línea dentro del mismo arranque de V2.
- Por esa absorción, los antiguos **`TEC-03` a `TEC-32`** corrieron un lugar y hoy son
  **`TEC-02` a `TEC-31`**.
- El antiguo **`FUT-03`** (*reactivar los vuelos de operador*) fue **retirado del backlog
  activo**: convertía una funcionalidad deliberadamente desactivada en trabajo pendiente de
  implementación. Su historia vive en `UC-P-17` y en la descripción de la épica 13.

---

## 8. Dependencias

Las **92 dependencias** van como texto en la línea `DEPENDE DE:` de cada descripción, con su
identificador funcional.

**No se importan como enlaces de Jira**: las columnas de enlace necesitan la clave de la
incidencia destino, que no existe antes de importar. Crear enlaces reales es un paso manual
posterior, opcional.
