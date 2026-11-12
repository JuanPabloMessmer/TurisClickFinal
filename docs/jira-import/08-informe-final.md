# Informe final — seguimiento Scrum de TurisClick en Jira

Proyecto **TurisClick (`TC`)**, *team-managed software*. Verificado el 09/11/2026.

---

# 1. RESULTADO

| Verificación | Esperado | Real | |
|---|---|---|---|
| Incidencias totales | 217 | **217** | ✅ |
| Épicas | 17 | **17** | ✅ |
| Ítems con épica padre | 200 | **200** | ✅ |
| Épicas con hijos | 17 | **17** | ✅ |
| Story · Task · Bug | 94 · 93 · 13 | **94 · 93 · 13** | ✅ |
| Estado `Done` | 163 | **163** | ✅ |
| Estado `To Do` | 37 ítems + 17 épicas | **54** | ✅ |
| Duplicados | 0 | **0** | ✅ |
| Sin prioridad | 0 | **0** | ✅ |
| Con etiquetas | 209 | **209** | ✅ |
| **Sprints creados** | 18 | **18** | ✅ |
| **Ítems con sprint** | 187 | **187** | ✅ |
| **Ítems sin sprint** | 13 | **13** | ✅ |
| **Sprint 7 vacío** | 0 ítems | **0** | ✅ |

**Sin discrepancias.** Claves `TC-1` a `TC-217`, asignadas de forma contigua: las
**17 épicas son `TC-1` a `TC-17`** y los **200 ítems son `TC-18` a `TC-217`**.

## Ítems por sprint

| Sprint | 1 | 2 | 3 | 4 | 5 | 6 | **7** | 8 | 9 |
|---|---|---|---|---|---|---|---|---|---|
| Ítems | 5 | 7 | 5 | 5 | 2 | 4 | **0** | 4 | 5 |

| Sprint | 10 | 11 | 12 | 13 | 14 | 15 | 16 | 17 | 18 |
|---|---|---|---|---|---|---|---|---|---|
| Ítems | 14 | 33 | 14 | 23 | 22 | 20 | 12 | 8 | 4 |

Suma: **187**.

---

# 2. CÓMO SE REPRESENTARON LOS SPRINTS RETROSPECTIVOS

Era la pregunta de fondo: **¿se puede representar un sprint pasado sin inventar su
historial de ejecución?** La respuesta verificada es **sí**.

Los 18 sprints se crearon por la API ágil de Jira y **los 18 quedaron en estado `future`**:

```
{"id":34,"state":"future","name":"Sprint 1",
 "startDate":"2026-04-01T09:00:00.000-04:00",
 "endDate":"2026-04-14T18:00:00.000-04:00",
 "createdDate":"2026-11-10T00:00:38.516Z"}
```

Lo que esto significa, y por qué es defendible:

- **`startDate` y `endDate` son las fechas *planificadas***, las del cronograma
  reconstruido. Jira las acepta sin exigir que correspondan al presente.
- **`createdDate` es la fecha real**, que Jira estampa por su cuenta: `2026-11-10`. No se
  puede alterar y **no se intentó**.
- **`state: "future"`** quiere decir que el sprint **nunca fue iniciado ni cerrado**. Jira
  no registra transiciones, ni fecha de inicio real, ni fecha de cierre real, **porque no
  ocurrieron**.

> **Ningún sprint se inició ni se cerró.** No existe burndown ni velocity, y eso es
> correcto: no hay ejecución real que medir. Lo que hay es una **estructura de
> planificación y trazabilidad**, no telemetría.

Cada sprint lleva además su objetivo en el campo `goal`, y los quince retrospectivos
incluyen la leyenda:

> *fechas estimadas; seguimiento consolidado retrospectivamente el 27/10/2026*

---

# 3. CÓMO SE ASIGNARON LOS ÍTEMS

**Por identificador funcional, no por etiqueta.** La asignación se construyó cruzando:

1. El **identificador funcional** de cada ítem (`UC-T-08`, `BUG-05`, `PEND-01`…), que vive
   en el prefijo del resumen de cada incidencia: `[UC-T-08] Reservar una experiencia…`.
2. La columna `Sprint` del CSV importado, que es la fuente de la distribución.

La correspondencia se **verificó contra Jira antes de ejecutar nada**: se tomaron 18 claves
de control repartidas por todo el rango (`UC-AUTH-01`→`TC-18`, `TEC-01`→`TC-113`,
`V1-01`→`TC-144`, `FUT-02`→`TC-217`, entre otras) y **las 18 coincidieron**.

La tabla completa, sprint por sprint y clave por clave, está en
[`07-sprints-y-claves.md`](07-sprints-y-claves.md).

---

# 4. LOS 13 ÍTEMS SIN SPRINT

Deliberado, y verificado uno por uno:

| Clave | Identificador | Motivo |
|---|---|---|
| `TC-28` | UC-P-03 | Marcado *Backlog* en `docs/use-cases.md`: nunca se implementó |
| `TC-205`…`TC-214` | DOC-01 … DOC-10 | Documento final de tesis: **sin fecha de entrega ni de defensa confirmadas** |
| `TC-216`, `TC-217` | FUT-01, FUT-02 | Mejoras futuras, fuera del cierre de noviembre |

Las **17 épicas tampoco llevan sprint**, que es el comportamiento normal de Jira: una épica
agrupa trabajo, no se ejecuta dentro de una iteración. Por eso la consulta
`sprint is EMPTY` devuelve **30** = 13 ítems + 17 épicas.

---

# 5. LIMITACIONES DE JIRA ENCONTRADAS

## 5.1 El importador CSV no acepta nombres de sprint

En la importación del 28/10 la columna `Sprint` falló entera, con 100 advertencias
(mostradas; afectaban a los 187) de una sola causa:

> `Sprint id must be a number: Sprint 10`

**El importador legado espera el ID numérico del sprint, no su nombre.** Como los sprints
todavía no existían, no había ID que poner.

**Resuelto así**: se crearon primero los 18 sprints, se leyeron sus IDs reales (34–51) y se
asignaron los ítems por la API ágil. Ningún ítem se borró ni se reimportó.

## 5.2 No hay burndown ni velocity, y no se fabricaron

Jira sólo produce esas métricas a partir de sprints **completados**, con transiciones
reales. Completar los sprints exigiría iniciarlos y cerrarlos **hoy**, lo que estamparía
fechas de ejecución falsas.

**No se hizo.** Es la limitación más importante para la tesis y conviene declararla:

> El seguimiento Scrum se consolidó retrospectivamente. Los sprints documentan la
> planificación y la trazabilidad del trabajo; no son un registro de telemetría de
> ejecución, porque el proyecto no se gestionó en Jira mientras se desarrollaba.

## 5.3 Las fechas de creación de las incidencias son las de la importación

Ninguna incidencia tiene una fecha de creación histórica: todas fueron creadas el 28/10/2026.
El CSV **no incluyó** las columnas `Created` ni `Resolved`, aunque el importador las admite
para administradores. Se decidió no fabricarlas.

La procedencia de cada ítem se declara por etiqueta: **163 ítems** llevan
`retro-2026-10-27`, y las etiquetas `evidencia-git`, `evidencia-doc`, `reconstruido`,
`fecha-estimada`, `fecha-por-validar` y `evidencia-parcial` distinguen en qué se apoya cada
uno.

## 5.4 Estimación desactivada

No hay story points. La función *Estimation* está apagada a propósito: no existen
estimaciones históricas verificables y no se inventaron. Se puede activar más adelante para
estimar el trabajo pendiente.

---

# 6. LO QUE NO SE VERIFICÓ

- **El contenido de las 217 descripciones** (criterios de aceptación, evidencia,
  dependencias). Se confirmó que el campo se mapeó e importó; no se revisaron una por una.
- **El orden de los ítems dentro de cada sprint** en el Backlog. Jira los ordena por rango;
  no se ajustó.

---

# 7. PENDIENTE DEL AUTOR

1. **Nombre del entrevistado E-3** → `TC-165` (`INV-09`), etiqueta `dato-pendiente`.
2. **Resultados de las entrevistas** → `TC-166` (`INV-10`), con la descripción
   deliberadamente en blanco.
3. **Fechas de entrega y defensa** → los diez ítems `DOC-` (`TC-205` a `TC-214`) siguen sin
   sprint hasta tenerlas.
4. **Validar las fechas estimadas de abril y mayo** → sprints 1 a 5.
