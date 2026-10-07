# Pagos y cancelación con reembolso

Qué cubre este documento: el libro de movimientos de dinero de una reserva, la política de cancelación que
configura el operador, y la orquestación que ejecuta una cancelación coordinada entre nuestra base, la
pasarela de pago y la aerolínea.

**Qué está implementado y qué no**, dicho antes que nada para que nadie lea de más:

| Implementado | Todavía no |
|---|---|
| Libro de movimientos inmutable (cobros, reversos y reembolsos) por reserva y por moneda | Pasarela de pago **real**: el cobro sigue siendo simulado |
| Política de cancelación por tramos, configurable por el operador y congelada en cada reserva | Autorización y captura reales con tarjeta |
| Presupuesto de cancelación por componente, con aceptación explícita | Emisión y reembolso de boletos en producción |
| Cancelación del pasaje en Duffel **modo de prueba**, en dos pasos | Conciliación financiera contable |
| Reembolsos idempotentes y resolución de lo que queda a medias | Política de cancelación en experiencias |

El cobro de TurisClick es **simulado**: no se mueve dinero, no se piden datos de tarjeta y no existe ningún
medio de pago guardado. El balance de Duffel en modo de prueba es otra cosa y no se mezcla con esto: es el
saldo ficticio con el que se declara el pago del pasaje ante la aerolínea de prueba.

---

## 1. Por qué un libro y no un estado

La tabla `payment_transactions` se agrega, nunca se modifica. La diferencia no es estética:

```
CHARGE   SUCCEEDED   548.91 USD
REFUND   SUCCEEDED    68.91 USD   (vuelo)
REFUND   SUCCEEDED   480.00 USD   (paquete)
```

Si el cobro de 548.91 se "convirtiera" en un reembolso de 548.91, se perdería que alguna vez se cobró ese
importe, y con eso la posibilidad de responder cuánto se cobró, cuánto se devolvió, qué queda efectivamente
pagado y **qué intento falló**. Un intento rechazado también es una fila: es la única forma de saber después
qué operación no funcionó.

No es software contable. No hay asientos dobles, ni cierres, ni conciliación bancaria. Es el mínimo que
permite explicar qué pasó con la plata de una reserva.

### Lo que el libro no guarda

Ni número de tarjeta, ni CVV, ni token, ni ningún dato del medio de pago. Lo único externo que puede guardar
es la referencia opaca que devuelve la pasarela. Un test verifica que la vista del operador no exponga ni esa
referencia ni la clave de idempotencia.

### Componente

Un **cobro** agrupa la reserva entera en una moneda —es una sola operación de pago— y no lleva componente.
Un **reembolso** siempre nace de un componente concreto (`PACKAGE`, `EXPERIENCE`, `FLIGHT`), y la base lo
exige con un CHECK, porque la política del operador y la de la aerolínea son eventos financieros distintos.

### Saldo

Siempre **por moneda**: cobrado, devuelto y neto. Nunca un total único entre monedas distintas, porque sumarlas
exigiría un tipo de cambio que TurisClick no tiene y no va a inventar.

---

## 2. Política de cancelación

El operador define tramos: *"con 30 días o más, 100%; con 15 o más, 50%; con menos, nada"*. Se guarda como
texto (`"30:100;15:50;0:0"`) por la misma razón que los orígenes de vuelo de un paquete: son dos a seis
tramos, y una tabla entera para eso agrega un join a cada lectura sin agregar una garantía.

**Resolución:** los tramos se evalúan de mayor a menor y gana el primero cuyo mínimo se alcance. Si ninguno
aplica, se devuelve 0 — una política que arranca en 15 días no dice nada sobre cancelar la noche anterior, y
en esa ausencia no se puede inventar un reembolso en nombre del operador.

**Validación:** dos tramos no pueden empezar el mismo día (es una contradicción, no una preferencia) y un
tramo más cercano a la salida no puede devolver **más** que uno más lejano (siempre es un error de carga).

**Sin política configurada, una reserva confirmada no se cancela desde la app.** No se asume 0% ni 100%: se le
dice a la persona que tiene que escribirle al operador. Es exactamente el comportamiento que existía antes de
esta oleada, ahora dicho con sus razones.

### El snapshot es lo que vuelve esto un contrato

Al reservar, la política se **copia** a `reservation_items.cancellation_policy`, igual que se copia el precio.
Si el operador la cambia mañana, la persona que compró hoy conserva la que aceptó. Un contrato que cambia solo
después de firmarlo no es un contrato — y hay un test que lo verifica editando el paquete y comprobando que la
reserva vieja sigue devolviendo el 100%.

**Las experiencias todavía no tienen política configurable**, así que una reserva confirmada que incluya una no
se cancela desde la app. La frontera es explícita, no un olvido.

---

## 3. Presupuesto de cancelación

`POST /api/reservations/{id}/cancellation-quote` calcula qué pasaría, lo **guarda** y no cancela nada.

Se persiste porque la aceptación tiene que atarse a *ese* cálculo: el precio del paquete lo decide una política
ya congelada, pero el reembolso del pasaje lo informa la aerolínea y puede cambiar o vencer. Sin una fila que
guarde lo que se mostró, "acepto" no significa nada verificable. **El cliente nunca manda un importe**: manda
el id del presupuesto.

Una línea por componente:

| Componente | De dónde sale el reembolso |
|---|---|
| Paquete / experiencia | La política congelada en la reserva, aplicada a los días de anticipación |
| Vuelo | Lo que informa la aerolínea al crear la cancelación pendiente |

`RefundKnown = false` significa que el proveedor **no informó** cuánto devuelve. No es lo mismo que devolver
cero: decirle a alguien que su pasaje es reembolsable sin que la aerolínea lo haya confirmado es prometer plata
ajena, así que la distinción viaja hasta la pantalla ("A confirmar", no "0.00").

**Vencimiento:** manda el plazo más corto entre nuestra ventana de 15 minutos y el `expires_at` que informa la
aerolínea. Pasado ese momento su cancelación ya no se puede confirmar, así que ofrecer el presupuesto sería
ofrecer algo inejecutable.

**Pedir uno nuevo invalida el anterior**, y tampoco es una decisión nuestra: Duffel sólo permite confirmar la
cancelación más reciente creada para una orden.

---

## 4. Orquestación de la cancelación

PostgreSQL, la pasarela y la aerolínea no comparten transacción. El orden está elegido por lo que queda roto si
algo falla en el medio.

```
  QUOTED ──(la persona acepta)──► ACCEPTED ──┬──► COMPLETED         todo cancelado y reembolsado
     │                                       ├──► REFUND_PENDING    cancelado; el reembolso quedó por reintentar
     │                                       ├──► REQUIRES_REVIEW   quedó a medias; lo resuelve una persona
     │                                       └──► FAILED            no se canceló nada; la reserva sigue viva
     └──(vence)──────────────────► EXPIRED
```

1. **Aceptación commiteada antes de tocar la aerolínea.** La reserva pasa `CONFIRMED → CANCELLING` con un
   `UPDATE` condicional y la cancelación queda `ACCEPTED`. Si el proceso muere después, queda rastro de una
   cancelación en curso en vez de una reserva que parece vigente con un pasaje cancelado. Como la transición es
   condicional, el doble toque termina ahí: la segunda ejecución no la gana y no cancela nada.
2. **La aerolínea, primero.** Si se liberara el cupo del paquete antes y la aerolínea rechazara, le habríamos
   regalado el lugar a otra persona mientras esta sigue con un pasaje: eso no se puede deshacer. Al revés sí —
   un pasaje cancelado sobre una reserva todavía viva es visible y resoluble.
3. **Una transacción local** cancela la reserva, pasa las líneas a `CANCELLED`, libera el cupo exactamente una
   vez y guarda lo que la aerolínea devolvió **de verdad** (lo confirmado manda sobre lo presupuestado).
4. **Los reembolsos al final**, con clave idempotente derivada de la cancelación y del componente.

### Qué pasa en cada falla

| Falla | Qué hace el sistema |
|---|---|
| No hay política para algún servicio activo | No se presupuesta: 409 y se invita a escribirle al operador |
| El presupuesto venció o quedó obsoleto | 409 con su código; no se cancela nada |
| Cancelar dos veces, o dos veces a la vez | Sólo una gana la transición; el cupo se libera una sola vez |
| La aerolínea **rechaza** la cancelación | No se cancela nada: la reserva vuelve a `CONFIRMED` y la cancelación queda `FAILED` |
| Desenlace **desconocido** con la aerolínea | `REQUIRES_REVIEW`: no se libera cupo ni se reintenta a ciegas |
| El reembolso falla | Lo cancelado sigue cancelado y la cancelación queda `REFUND_PENDING`. **No se finge que la plata volvió** |
| El proceso muere a mitad de camino | Queda `ACCEPTED`/`REQUIRES_REVIEW` y lo retoma la resolución |

### Resolución de lo que quedó a medias

`CancellationResolutionBackgroundService` cada 60 s (apagable por configuración, y apagado en los tests) toma
las cancelaciones en `ACCEPTED`, `REQUIRES_REVIEW` o `REFUND_PENDING`.

- **Falta sólo la plata** → se reintenta el reembolso, que es idempotente por clave.
- **Falta saber si el pasaje quedó cancelado** → se le **pregunta** al proveedor por la orden (`cancelled_at`)
  antes de reintentar nada. Si ya está cancelada, se completa la parte local; si no, se reintenta la
  confirmación con espera creciente (1, 5 y 15 minutos) hasta 4 intentos.
- **Se agotaron los intentos** → la reserva vuelve a `CONFIRMED` (no se canceló nada) y queda
  `REQUIRES_REVIEW` para que una persona lo resuelva. Esto no se disimula como éxito.

---

## 5. Multi-moneda

La regla del dominio no cambia: **nunca se suman importes de monedas distintas**. Si el paquete se cobra en
bolivianos y el pasaje en dólares, el presupuesto muestra dos reembolsos, el libro guarda dos filas y la
pantalla dice por qué no hay un único total. Si la aerolínea informara un reembolso en una moneda distinta a la
cobrada, se trata como importe **no informado** y se dice, que es lo único honesto que se puede hacer sin un
tipo de cambio.

---

## 6. Qué ve cada rol

- **Turista:** el desglose completo antes de aceptar, y después el resultado real —incluido "reembolso en
  proceso" o "en revisión"—. La reserva cancelada **sigue visible** en Mis viajes con lo que se le devolvió.
- **Operador:** que le cancelaron, con qué política se aplicó a *esa* reserva (que puede no ser la que tiene
  configurada hoy) y cuánto se reembolsó de su línea. Nada del medio de pago, porque no existe.
- **Admin:** `GET /api/admin/reservations/{id}/payments` (libro completo, saldo por moneda y cancelaciones) y
  `GET /api/admin/cancellations` (la cola de lo que quedó a medias, lo más viejo primero). Es una vista de
  operación, no un panel de finanzas, y no cambia estados: resolver una cancelación es una operación de
  dominio, no una edición manual de la plata.

---

## 7. Evidencia

La corrida real contra Duffel en modo de prueba —búsqueda, emisión, cancelación y reembolso por componente—
está en [`package-flight-e2e-results.md`](package-flight-e2e-results.md). Los tests automáticos nunca llaman a
Duffel: usan el proveedor falso, que reproduce los casos difíciles (no reembolsable, reembolso no informado,
cancelación rechazada, orden ya cancelada) por aeropuerto de origen.
