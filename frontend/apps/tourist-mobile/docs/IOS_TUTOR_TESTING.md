# Probar TurisClick en un iPhone

Guía para probar la app de turistas de TurisClick en un iPhone, sin instalar nada desde la App Store más
que **Expo Go**. Lleva unos 10 minutos la primera vez.

La app se conecta a un servidor de demostración en Azure. **Nada de lo que hagas es real:** no se cobra
ningún pago, no se emite ningún pasaje y no hace falta ninguna tarjeta.

## 1. Preparar el teléfono (una sola vez)

1. Instalá **Expo Go** desde la App Store y, si ya lo tenías, actualizalo a la última versión.
2. Creá una cuenta gratuita en <https://expo.dev/signup> (o desde la misma app Expo Go).
3. Pasale a Juan Pablo el correo o el usuario de esa cuenta. Te va a llegar por correo una **invitación a
   la organización** del proyecto: abrila y aceptala.
4. Abrí Expo Go e **iniciá sesión** con esa cuenta.

> Los pasos 2 a 4 son obligatorios: Expo Go sólo abre proyectos de una organización de la que tu cuenta
> es miembro. Sin sesión iniciada o sin aceptar la invitación, la app no carga.

## 2. Abrir la app

Juan Pablo te va a enviar un **enlace** y un **código QR**.

- Con el QR: abrí la **Cámara** del iPhone, apuntá al código y tocá el aviso que aparece.
- Con el enlace: tocá el enlace desde el mismo iPhone.

En los dos casos se abre Expo Go y descarga TurisClick. Si iOS pregunta si querés abrir el enlace en
Expo Go, aceptá.

**La primera carga puede tardar hasta un minuto.** El servidor se duerme cuando nadie lo usa y tarda en
despertarse. Si ves un error de conexión o la pantalla queda cargando, esperá un minuto y volvé a
intentar: la segunda vez suele responder enseguida.

Para confirmar que estás en la versión correcta: pestaña **Perfil**, al pie debe decir
`servidor Azure`.

## 3. Crear tu cuenta de turista

En la pantalla de ingreso tocá **Crear cuenta** y registrate con un correo y una contraseña cualquiera
(pensada sólo para esta prueba; no uses una contraseña que uses en otro lado). Después la app te hace unas
preguntas breves sobre tus preferencias de viaje.

Esta cuenta es de TurisClick y no tiene nada que ver con la de Expo del paso 1.

## 4. Qué probar

No hace falta seguir un orden. Lo que más nos sirve que recorras:

| # | Qué | Dónde | Qué mirar |
|---|---|---|---|
| 1 | **Explorar** | Pestañas **Inicio** y **Explorar** | Que carguen destinos con fotos; buscar y filtrar. |
| 2 | **Experiencias y paquetes** | Tocá cualquier tarjeta | Que el detalle sea claro: precio, qué incluye, fechas. |
| 3 | **Reserva** | En un detalle, **Elegir fecha** | Elegir una fecha en el calendario, la cantidad de personas y confirmar. |
| 4 | **Pago simulado** | La pantalla que sigue a la reserva | Pagar. Es un pago de mentira: no pide tarjeta. La reserva debe quedar en **Mis viajes**. |
| 5 | **Asistente e itinerarios** | Pestaña **Asistente** | Escribile, por ejemplo, «Voy 4 días a La Paz con mi pareja». Pedile cambios a la propuesta, tocá **¿Por qué?**, guardá el itinerario y probá reservarlo. |
| 6 | **Vuelos (DEMO)** | Pestaña **Vuelos** | Buscar un vuelo. Los resultados llevan el rótulo **DEMO**: son vuelos simulados, no existen. |
| 7 | **Mis viajes** | Pestaña **Mis viajes** | Abrir una reserva, ver su detalle y probar cancelarla. |

El asistente puede tardar varios segundos en responder: es normal.

## 5. Si algo falla

Avisale a Juan Pablo (por el medio que usen habitualmente) con:

- **Qué estabas haciendo**, paso a paso, y qué esperabas que pasara.
- **Una captura de pantalla** (botón lateral + subir volumen).
- **La hora aproximada** en que ocurrió.
- El texto que aparece al pie de **Perfil** (versión y servidor).

También sirven los comentarios que no son errores: algo que no se entiende, un texto confuso, un paso
que te resultó incómodo.

Si la app se cierra sola o queda en una pantalla roja o en blanco, sacá la captura antes de cerrarla.

## 6. Recibir una versión nueva

Cuando Juan Pablo avise que hay una actualización:

1. Cerrá TurisClick por completo: deslizá hacia arriba desde el borde inferior y quitá **Expo Go** de las
   apps abiertas.
2. Volvé a abrir el mismo enlace o QR (o entrá a Expo Go y elegí TurisClick en la lista de recientes).

Expo Go descarga la versión nueva al abrir. No hay que reinstalar nada.

## Problemas frecuentes

| Qué ves | Qué hacer |
|---|---|
| Expo Go dice que no tenés acceso al proyecto, o no lo encuentra | Revisá que iniciaste sesión en Expo Go con la cuenta invitada y que aceptaste la invitación del correo. |
| Expo Go dice que el proyecto usa una versión de SDK incompatible | Avisale a Juan Pablo y, mientras tanto, **no actualices Expo Go** otra vez: es un problema de versiones que se resuelve de su lado. |
| «No se pudo conectar» o carga interminable al entrar | Es el servidor despertándose. Esperá un minuto y reintentá. |
| El QR abre el navegador y no Expo Go | Comprobá que Expo Go esté instalado y abrí el enlace desde el iPhone. |
