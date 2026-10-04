# Selección del modelo local para el agente de TurisClick

Este documento explica **qué modelo corre el agente cuando `AI__Provider=Ollama`, por qué ese y no otro**, y qué queda como alternativa. No es un ranking general de LLMs: es una decisión para un caso de uso concreto, una máquina concreta y un tribunal concreto.

Lo que el modelo tiene que hacer en TurisClick es acotado: leer una frase en español (a veces sin tildes, a veces en inglés), devolver un JSON con la intención, y ordenar/explicar una lista de candidatos que **ya vienen de PostgreSQL**. No tiene que saber de turismo, no tiene que recordar precios y no decide nada que el backend no revalide después (ver [`ai-rag-architecture.md`](ai-rag-architecture.md)). Eso baja muchísimo la barra de tamaño: no necesitamos el modelo más grande, necesitamos el más obediente y el más estable.

## 1. Restricciones reales

### Hardware (medido en la máquina de desarrollo, 2026-09-27)

| Recurso | Valor real |
|---|---|
| CPU | Intel Core i5-14400F (10 núcleos: 6P + 4E, sin iGPU) |
| RAM total | 16 GB (`16.984.227.840` bytes) |
| RAM libre en uso normal | **~0,8 GB** con el entorno de trabajo abierto (VS/Rider, Expo, navegador) |
| GPU | AMD Radeon RX 9060 XT, **15,9 GB de VRAM** (RDNA 4) |
| Ollama | **no instalado todavía** |

Dos consecuencias que definen la elección:

1. **La RAM libre es el recurso escaso, no la VRAM.** Un 7B cuantizado a Q4 ocupa ~4,7 GB. En CPU eso vive en RAM del sistema, y hoy no hay 4,7 GB libres sin cerrar el entorno de desarrollo. En GPU vive en los 15,9 GB de VRAM, que están casi enteros disponibles. **El objetivo es que el modelo entre entero en VRAM**, y el presupuesto real es "≤ 10 GB de pesos + contexto", no "16 GB de RAM".
2. **La aceleración AMD en Windows hay que verificarla, no asumirla.** Ollama acelera GPUs AMD vía ROCm/HIP en Windows para una lista de targets `gfx`; la RX 9060 XT es RDNA 4, arquitectura reciente, y el soporte depende de la versión de Ollama instalada. Puede salir acelerado de fábrica, puede necesitar una versión nueva, o puede caer a CPU. Por eso la selección **no puede apostar todo a la GPU**: el modelo elegido tiene que ser usable también en CPU, aunque más lento. El paso de verificación está en [`ollama-local-setup.md`](ollama-local-setup.md).

### Requisitos funcionales

| Requisito | Por qué manda |
|---|---|
| Español rioplatense/boliviano coloquial, con y sin tildes | Es el idioma real de los usuarios del demo |
| Inglés razonable | Es justamente el hueco del baseline determinístico (ver [`ai-evaluation.md`](ai-evaluation.md)) |
| Seguimiento de instrucciones | El prompt le prohíbe inventar; un modelo desobediente rompe la premisa |
| JSON estructurado confiable | Todas las operaciones de `IAiModelClient` devuelven JSON validado contra un schema |
| Razonamiento suficiente | Repartir 3–8 candidatos en 2–5 días sin repetir ni dejar huecos. No es razonamiento matemático |
| Latencia tolerable en demo | El asistente es conversacional: más de ~15 s por turno arruina la demostración |
| Licencia limpia | Es un proyecto académico que además se muestra como producto |
| Estabilidad | En una defensa de tesis, "a veces funciona" es peor que "funciona un poco peor" |

Lo que **no** es requisito: contexto largo (se le manda un puñado de candidatos, no el catálogo), capacidad de código, multimodalidad, y "conocimiento del mundo" (los datos los pone Postgres).

## 2. Candidatos evaluados

Se compararon las tres familias pedidas, en los tamaños que entran en el presupuesto. Todos están en la biblioteca de Ollama, así que "compatibilidad" no es diferenciador: lo que diferencia es licencia, calidad en español, disciplina de formato y tamaño.

| Modelo (tag de Ollama) | Params | Q4_K_M aprox. | Licencia | Español | Instrucciones / JSON | Notas |
|---|---|---|---|---|---|---|
| **`qwen2.5:7b-instruct`** | 7,6B | ~4,7 GB | **Apache 2.0** | Declarado y bueno; multilingüe entrenado sobre ~29 idiomas | Muy fuerte; la familia se entrenó explícitamente para salida estructurada y JSON | Contexto 128K (no lo necesitamos). El más citado como "el 7B obediente" |
| `qwen2.5:14b-instruct` | 14,8B | ~9,0 GB | Apache 2.0 | Mejor que el 7B | Mejor que el 7B | Entra en VRAM, **no** entra cómodo en CPU. Duplica latencia por poca ganancia en tareas tan acotadas |
| `qwen2.5:1.5b-instruct` | 1,5B | ~1,0 GB | Apache 2.0 | Aceptable, se degrada en frases largas | Suficiente con schema; se equivoca más | Opción de emergencia si la GPU no acelera y el 7B en CPU resulta intolerable |
| `qwen3:8b` | 8,2B | ~5,2 GB | Apache 2.0 | Igual o mejor que Qwen2.5-7B | Bueno, **pero** tiene modo "thinking" híbrido | El razonamiento en voz alta hay que apagarlo explícitamente; si se filtra, contamina el JSON y multiplica la latencia. Riesgo innecesario para un demo |
| `llama3.1:8b` (Instruct) | 8,0B | ~4,9 GB | **Llama 3.1 Community License** (no OSI: cláusula de 700M MAU, obligación de atribución "Built with Llama") | Español oficialmente soportado, bueno | Bueno; algo más verboso, tiende a agregar texto alrededor del JSON | Familia distinta (otro tokenizer, otro sesgo de formato): buena segunda opinión |
| `mistral:7b-instruct` (v0.3) | 7,2B | ~4,4 GB | Apache 2.0 | Funciona pero es el más débil de los tres en español | El más flojo siguiendo instrucciones negativas ("no inventes") | Generación anterior; su fuerza era la velocidad, no la obediencia |
| Ministral 8B | 8B | — | **Mistral Research License (no comercial)** | — | — | **Descartado por licencia** |
| Mistral Small 3.x | 24B | ~14 GB | Apache 2.0 | Muy bueno | Muy bueno | **Descartado por tamaño**: llena la VRAM y deja la latencia fuera de rango conversacional en esta máquina |

Gemma y Phi quedan fuera del alcance pedido; Gemma además arrastra términos de uso propios, lo que la haría una discusión extra en la defensa sin ganancia técnica acá.

## 3. Decisión

### Modelo principal: `qwen2.5:7b-instruct`

Gana por la suma de cuatro cosas, no por una sola:

1. **Licencia Apache 2.0**, sin cláusulas de escala ni obligaciones de atribución. Es la única de las tres familias que da eso en este tamaño sin asteriscos.
2. **Disciplina de formato.** El agente no le pide creatividad: le pide un JSON que valida contra un schema (`AiJsonSchemas`). Qwen2.5-Instruct es la familia de este rango que mejor se comporta con salida estructurada, y eso se traduce directo en menos reintentos y menos caídas al fallback.
3. **Español sin concesiones.** Multilingüe por diseño, no "inglés con español de regalo". El dataset del benchmark es mayormente español coloquial boliviano; ahí es donde el modelo tiene que rendir.
4. **Entra dos veces.** ~4,7 GB entran holgados en 15,9 GB de VRAM (con contexto y margen para que Windows respire) **y** entran en RAM si hay que cerrar un par de aplicaciones y correr en CPU. Ningún otro candidato de calidad comparable cumple las dos.

**El default en código ya era `qwen2.5:7b-instruct`** (`AiOptions.OllamaOptions.Model`). La auditoría de esta fase lo confirma, así que **no se cambia el default** y no hay cambio de comportamiento que testear. La alternativa se elige por configuración, sin recompilar:

```bash
AI__Ollama__Model=llama3.1:8b
```

### Modelo alternativo: `llama3.1:8b`

Se elige alternativa **de otra familia**, no el hermano grande del principal. Si el problema es "este modelo no sigue mi prompt", subir de 7B a 14B dentro de la misma familia repite los mismos sesgos de formato; cambiar de familia los rompe. Llama 3.1 8B tiene otro tokenizer, otro post-entrenamiento y otra tendencia de verbosidad, así que es una segunda opinión real. Cuesta 200 MB más y soporta español oficialmente.

Su desventaja es la licencia: la Llama 3.1 Community License no es OSI, exige atribución visible ("Built with Llama") y tiene una cláusula de 700M de usuarios activos mensuales — irrelevante para el alcance de la tesis, pero es una condición que Apache 2.0 no impone. Por eso es la alternativa y no la principal.

### Plan C, sólo si la GPU no acelera: `qwen2.5:1.5b-instruct`

Si Ollama termina corriendo en CPU y el 7B da turnos de más de ~30 s con la máquina cargada, el 1.5B (Apache 2.0, ~1 GB) mantiene el demo vivo: con el schema puesto sigue devolviendo JSON válido, se equivoca más en extracción, y **el fallback determinístico y los guardrails atrapan cada equivocación que importa**. Es degradación de calidad, no de seguridad.

## 4. Latencia y calidad: medido

Ollama quedó instalado el 2026-10-04 y `ollama ps` reporta **100% GPU**: el modelo entra entero en los 15,9 GB de VRAM, como anticipaba el análisis. Estos números son mediciones reales sobre los 33 casos del benchmark, no estimaciones (el detalle completo, con el antes y el después de cada cambio de integración, está en [`ai-evaluation.md`](ai-evaluation.md)):

| Escenario | Medido |
|---|---|
| Extracción de preferencias (mediana) | ~1,8 s por turno |
| Refinamiento (mediana) | ~0,8 s |
| Composición de itinerario (p95) | ~4,7 s |
| Primer turno tras levantar el modelo | ~10 s la primera vez; `AI__Ollama__KeepAlive=10m` evita pagarlo de nuevo |
| Reproducibilidad con `temperature 0` | **exacta**: dos corridas completas dieron 0 campos distintos |
| Aciertos | 172/179 (96%) el LLM, 174/179 (97%) el híbrido, contra 170/179 (95%) del baseline |

El timeout está en 60 s (`AI__Ollama__TimeoutSeconds`) y, al vencerse, el usuario **no ve un error**: ve la respuesta determinística (`FallbackAiModelClient`). Esa es la razón por la que se puede probar un modelo local sin arriesgar la demo.

**La primera medición dio 83%, no 96%.** La diferencia no fue de modelo: fue un `required` incompleto en nuestros JSON Schemas, que le permitía al modelo omitir campos en vez de contestarlos. Vale la pena leer esa parte de [`ai-evaluation.md`](ai-evaluation.md) antes de evaluar cualquier modelo: un LLM mal integrado parece un LLM malo.

## 5. Cómo se verifica esta decisión

La decisión no se defiende con opiniones: se defiende con el benchmark, sobre el mismo dataset y las mismas métricas que el baseline.

```bash
# principal
dotnet run --project tools/ai-benchmark -- --with-llm --model qwen2.5:7b-instruct

# alternativa
dotnet run --project tools/ai-benchmark -- --with-llm --model llama3.1:8b
```

Se compara contra el baseline determinístico ya medido (170/179 = 95%, 0 JSON inválido, 0 datos inventados, mediana 0 ms) mirando cuatro cosas:

1. **Los tres casos en inglés**, que el baseline falla por diseño. Si el LLM no los gana, no está aportando lo que se le pide.
2. **JSON inválido e ids inventados.** El baseline tiene 0 de ambos. El LLM se mide **sin** fallback justamente para que se vea cuántos comete.
3. **Que no pierda lo que el baseline acierta** — sobre todo los casos `adversarial-*`: un prompt inyectado no debe mover presupuesto, viajeros, destino, precios ni ids.
4. **Latencia real** (mediana y p95), que es la métrica que decide si el modelo es usable en vivo.

Si el 7B queda por debajo del baseline en aciertos **y** por encima en latencia, la conclusión honesta de la tesis es que para este dominio tan acotado las reglas alcanzan y el LLM aporta sobre todo idioma y tolerancia a lenguaje libre. Ese resultado también es un resultado; el benchmark está hecho para poder decirlo.

**Resultado de esa verificación (2026-10-04):** el modelo principal quedó confirmado. Gana los tres casos en inglés, mantiene 0 JSON inválido y 0 datos inventados, resiste los tres prompts adversariales, y es reproducible. Cuesta ~1,8 s por turno contra 0 ms del baseline, así que el proveedor sigue siendo una decisión de configuración y no de arquitectura. El mejor puntaje lo da `Hybrid` (el mismo modelo con las reglas ganando en los escalares literales), que es el proveedor recomendado para local.
