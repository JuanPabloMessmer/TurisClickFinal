# Deuda de interfaz

Lo que quedó pendiente en las dos interfaces después de la oleada de completitud de producto. No es una lista
de deseos: cada entrada es algo que ya se notó mirando el producto funcionando, con el motivo por el que no se
arregló ahora. Si algo no está acá es porque nadie lo vio, no porque esté perfecto.

Orden: primero lo que un usuario nota, después lo que molesta al operar, al final lo interno.

---

## Lo que un usuario nota

### La base de desarrollo contamina el catálogo que ve el administrador

La base de desarrollo comparte servidor con las pruebas de integración y los E2E, así que el listado global de
experiencias y el selector de categorías del operador muestran residuos de prueba (`Interés-Ai-…`,
`Tour O8 …`, `Operador E2E …`, empresas `Gate Visual …`).

**Por qué no se arregló:** borrar datos de una base es una operación destructiva y no se hace por conveniencia
de una demo. La salida correcta es una base limpia, documentada en el [runbook](demo-runbook.md#2-preparar-la-base-de-datos).
Lo que sí haría falta a futuro es que las pruebas usen un esquema propio y lo dejen limpio al terminar.

### Nueve destinos del seed no tienen foto

Cabezas, Ivirgarzama, Puerto Villarroel, San Lucas y algunos más muestran el fondo neutro de la app.

**Por qué no se arregló:** no hay en Wikimedia Commons una foto con licencia libre que **represente al
destino**. Lo que había era el hall de un aeropuerto, una polilla de museo, tres cementerios, una imagen
satelital del INPE y un pez de Cabo San Lucas (México). Se rechazaron a propósito y queda documentado en
`tools/demo-catalog/destination-images.manifest.json` bajo `rejected`. El fondo neutro es más honesto que una
foto que no es del lugar. Conseguir fotos propias con permiso es trabajo de contenido, no de código.

### Las categorías del catálogo no tienen descripción

Las ocho categorías reales (`Aventura`, `Gastronomía`, …) tienen `description = null`, así que el backoffice
muestra `—` y la app no puede explicar qué incluye cada interés.

**Por qué no se arregló:** es contenido editorial y hay que escribirlo bien una vez. El campo ya existe en el
DTO y en la base; sólo falta el texto.

### La política de cancelación no se ve en el checkout

El detalle del paquete ya muestra los tramos de reembolso antes de pagar. La pantalla de pago, no: ahí el
viajero ya no los tiene a la vista en el momento de confirmar.

**Por qué no se arregló:** repetirla en el checkout es la decisión correcta pero toca el flujo de pago, que es
lo más delicado de la app. Merece su propio cambio con sus propias pruebas.

### El precio del vuelo y el del paquete conviven sin un total claro

Cuando un paquete incluye vuelo, el viajero ve dos importes en monedas que pueden ser distintas (el paquete en
BOB, el pasaje en la moneda que devuelve la aerolínea) y no hay un total.

**Por qué no se arregló:** sumar monedas distintas está prohibido por el dominio y no existe conversión en
TurisClick. Un total honesto exigiría una tasa de cambio con fuente y fecha, que es una decisión de negocio, no
de interfaz.

---

## Lo que molesta al operar

### El panel del operador resume sólo sus 20 experiencias más recientes

Si tiene más, "Hoy" lo dice y manda a Experiencias. Un operador con 50 experiencias no ve en Hoy qué fechas
tiene abiertas en las otras 30.

**Por qué no se arregló:** resolverlo bien es un endpoint de resumen que calcule las próximas salidas en el
servidor, no 50 consultas desde el cliente.

### Las tablas del backoffice no se usan bien en un teléfono

A 375 px las tablas de Experiencias, Paquetes y Reservas se desbordan horizontalmente. Se puede leer, pero
arrastrando.

**Por qué no se arregló:** el backoffice es una herramienta de escritorio y nadie da de alta un paquete desde
el teléfono. Convertir cada tabla en tarjetas apiladas es un rediseño por pantalla.

### El listado global de reservas no pagina visiblemente

Trae una página y no ofrece ir a la siguiente.

**Por qué no se arregló:** con el volumen de la demo no se nota. Hace falta el control de paginación que ya
tienen otras pantallas.

### Las cancelaciones que quedan pendientes dependen de un servicio en proceso

`CancellationResolutionBackgroundService` corre dentro de la API. En local funciona; en App Service gratuito la
app se duerme y una cancelación pendiente puede tardar. El administrador puede forzar la resolución desde la
ficha de la reserva, así que no hay nada bloqueado, pero la espera no se explica en la pantalla.

**Por qué no se arregló:** la decisión de fondo (trabajo agendado o `Always On`) es de infraestructura y esta
oleada no toca infraestructura.

---

## Lo interno

### El nombre accesible de los controles se declara a mano

`Button`, `Chip` y `AuthSwitchLink` llevan `accessibilityLabel` explícito porque el texto anidado no siempre
llega a nombrar al control en el árbol de accesibilidad de la web. Funciona, pero es una regla que hay que
recordar en cada control nuevo.

**Por qué no se arregló:** lo correcto es una prueba que recorra las primitivas y falle si una queda sin
nombre, parecida a la que ya impide que los códigos internos lleguen a la pantalla.

### Tourist Mobile no tiene lint

El Backoffice corre `oxlint` y falla con errores. La app móvil sólo corre `tsc --noEmit`: no tiene ESLint
instalado ni configurado.

**Por qué no se arregló:** se probó. Con `eslint-config-expo` la app reporta **23 errores** en código que hoy
funciona y está cubierto por 406 pruebas: 10 `no-undef`, 6 `react-hooks/refs`, 6
`react-hooks/set-state-in-effect` y 1 `react-hooks/globals`. Varios son reales (`useCountdown` y
`PackageFlightSection` llaman `setState` dentro de un efecto), pero arreglarlos es refactorizar hooks que
andan, y no es algo para hacer encima de una demo. Adoptar el lint es un cambio propio: instalar las dos
dependencias, resolver los 23 hallazgos y recién entonces sumarlo al gate. Mientras tanto quedó **sin
instalar** a propósito, para no dejar un lint que nadie corre porque siempre falla.

### No hay revisión visual automatizada

No existen capturas de referencia. Cada oleada se revisa a mano, y eso encuentra lo que se mira.

**Por qué no se arregló:** montar capturas de referencia para dos plataformas es un proyecto en sí mismo, y con
el producto todavía cambiando de forma generaría más ruido que señal.

### Las pantallas privadas de la app no se revisaron renderizadas

Mis viajes, el detalle de reserva, el presupuesto de cancelación, el checkout y el chat del asistente se
verifican sólo con pruebas automatizadas. Se intentó revisarlos en el build web, pero `expo-secure-store` no
existe en web (la sesión queda sólo en memoria) y las pantallas de tabs quedan montadas en el DOM al mismo
tiempo, así que lo que se lee no corresponde a lo que se ve.

**Por qué no se arregló:** hace falta un emulador o un teléfono con Expo Go. Las pruebas cubren el
comportamiento; lo que falta es la mirada sobre el resultado.
