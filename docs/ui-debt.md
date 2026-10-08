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

**Resuelto para la demostración, no en la raíz.** Borrar datos de una base es destructivo y no se hace por
conveniencia de una demo. En la Oleada 14 se agregó `tools/demo/setup-clean-demo.ps1`, que crea una base
aparte, la migra y le carga el catálogo curado: ahí el validador da 859/859, contra 6 fallas sobre la base de
desarrollo.

**Lo que sigue pendiente:** que las pruebas de integración y los E2E usen un esquema propio y lo dejen limpio
al terminar, en vez de acumular fixtures en la base con la que se trabaja. Y que los scripts de validación de
escenarios no escriban en la base de demostración —hoy sí lo hacen, y hay que reconstruirla con `-Recreate`
después de usarlos.

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
app se duerme sin tráfico y, mientras duerme, ningún timer corre.

> **Corrección de la Oleada 13.** Ahí escribí que "el administrador puede forzar la resolución desde la ficha
> de la reserva". Era falso: existía `GET /api/admin/cancellations` para **ver** la cola y nada para actuar
> sobre ella. En la Oleada 14 se agregó `POST /api/admin/cancellations/resolve`, que reintenta la cola en el
> momento. Llama a la misma resolución que corre sola, que ya era idempotente, y devuelve cuántas había,
> cuántas completó y cuántas quedan.

Lo que todavía falta: la espera no se explica en la pantalla del viajero, y el Backoffice no tiene un botón
para ese endpoint —hoy se invoca a mano.

**Por qué no se arregló del todo:** la decisión de fondo (trabajo agendado o `Always On`, que requiere plan
pago) es de infraestructura, y estas oleadas no la tocan. Está planteada en
[deployment-readiness.md](deployment-readiness.md#5-servicios-de-fondo-el-riesgo-real).

---

## Lo interno

### El formato de la plata se unificó, pero a mano

En la Oleada 14 se corrigieron once lugares que escribían `BOB 3000.00` al lado de un `Bs 3.000,00` del mismo
importe: el resumen de cancelación del backend, la tarjeta de viaje cancelado, el detalle de reserva, el
checkout, los cambios de precio del asistente y tres tablas del Backoffice. Todo pasa por `formatCurrency` (o
`formatAmount`, cuando la moneda ya está en el encabezado).

**Lo que falta:** nada impide que el próximo `${currency} ${amount.toFixed(2)}` vuelva a entrar. Haría falta
una regla de lint o un chequeo estático como el que ya existe para los códigos internos y para los valores de
enum.

### El nombre accesible de los controles se declara a mano

`Button`, `Chip` y `AuthSwitchLink` llevan `accessibilityLabel` explícito porque el texto anidado no siempre
llega a nombrar al control en el árbol de accesibilidad de la web. Funciona, pero es una regla que hay que
recordar en cada control nuevo.

**Por qué no se arregló:** lo correcto es una prueba que recorra las primitivas y falle si una queda sin
nombre, parecida a la que ya impide que los códigos internos lleguen a la pantalla.

### No hay revisión visual automatizada

No existen capturas de referencia. Cada oleada se revisa a mano, y eso encuentra lo que se mira.

**Por qué no se arregló:** montar capturas de referencia para dos plataformas es un proyecto en sí mismo, y con
el producto todavía cambiando de forma generaría más ruido que señal.

### La app no se revisó en un Android real

En la Oleada 14 sí se revisaron renderizadas las pantallas privadas, en el build web: Mis viajes con sus tres
estados, el detalle de reserva, el presupuesto de cancelación, el asistente autenticado, el perfil y los cinco
pasos de preferencias. El problema de "todas las tabs quedan montadas" se resolvió respetando el
`aria-hidden="true"` con que react-navigation marca las pantallas inactivas, así que lo que se lee **es** lo
que está en pantalla.

Lo que sigue sin verificarse, porque el build web no puede:

- **La persistencia de sesión.** `expo-secure-store` no existe en web: la sesión vive sólo en memoria y se
  pierde al recargar o al vencer el access token (15 minutos). En el dispositivo usa el Keystore.
- **El teclado nativo**, y si tapa el campo activo en el formulario de pasajeros.
- **Los gestos.** Los `Pressable` que usan el sistema de respondedores de React Native no reaccionan a eventos
  de puntero sintéticos, así que las tarjetas de opción del onboarding se pudieron **ver** pero no **tocar**.
- **Safe areas en un teléfono con notch**, y las fuentes y densidades reales.

**Por qué no se arregló:** esta máquina no tiene el SDK de Android, ni emulador, ni `adb`. Hace falta un
emulador o un teléfono con Expo Go. El paso 14 del
[plan de despliegue](azure-v2-deployment-plan.md#14-smoke-tests-en-android) enumera exactamente qué verificar
cuando haya uno.
