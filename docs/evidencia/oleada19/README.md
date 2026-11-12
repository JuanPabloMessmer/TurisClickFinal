# Evidencia — Oleada 19

Material de respaldo de la validación funcional documentada en
[`../../oleada19-validacion-funcional.md`](../../oleada19-validacion-funcional.md) y
[`../../oleada19-informe-cierre.md`](../../oleada19-informe-cierre.md).

Se conserva acá porque originalmente vivía en un directorio temporal del sistema, que no
sobrevive indefinidamente.

---

## Procedencia

| Archivo | Generado | Qué es |
|---|---|---|
| `evidencia-o19.json` | 09/11/2026 22:03 | Respuestas HTTP crudas de los 13 pasos de la batería automática (T-01..T-13) contra el staging local |
| `bateria_o19.py` | 09/11/2026 22:04 | El script que ejecutó esos 13 pasos |
| `s2_aislamiento.py` | 09/11/2026 21:57 | Prueba S-2: un token emitido por staging es rechazado por Azure V2 |
| `run_staging.py` | 09/11/2026 22:00 | Lanza la API con la configuración de staging inyectada por variables de entorno |
| `staging_db.py` | 09/11/2026 21:56 | Crea la base de staging local; aborta si el host no es local |

Todos se generaron sobre el commit **`2d75b5d`**, contra:

- **Staging local** — `http://localhost:5299` + PostgreSQL `turisclick_staging_o19`
- **Azure V2** — sólo lectura, únicamente en `s2_aislamiento.py` para comprobar el rechazo del token

## Propósito

Respaldar las **25 pruebas ejecutadas en staging local** (13 funcionales + 5 de aislamiento, de
las cuales S-5 no se ejecutó) y las **15 verificaciones de sólo lectura en Azure V2**.

**No respalda las 8 pruebas manuales M-01..M-08**, que las ejecutó el usuario en su iPhone y de
las que no hay captura ni grabación. Su único registro es el reporte del usuario, asentado en §4
del documento de validación, más la corroboración indirecta leída de la base (§4.1).

---

## Redacciones aplicadas

Los resultados de las pruebas **no se alteraron**. Se reemplazaron únicamente valores de
credenciales, con reemplazo textual puntual para que el resto quede idéntico.

| Archivo | Qué se redactó | Ocurrencias |
|---|---|---|
| `evidencia-o19.json` | accessToken (JWT firmado) | 4 |
| `evidencia-o19.json` | refreshToken | 4 |
| `evidencia-o19.json` | temporaryPassword generada por el backend | 1 |
| `bateria_o19.py` | contrasenas de las cuentas ficticias de prueba | 2 |
| `run_staging.py` | literal del que se derivaba la clave JWT de staging | 1 |
| `s2_aislamiento.py` | nada: no contiene secretos | — |
| `staging_db.py` | nada: no contiene secretos | — |

### Por qué

El repositorio tiene remoto en GitHub, así que nada que sea una credencial debe entrar en la
historia, incluso si ya no sirve.

Sobre el riesgo real de lo redactado, para que quede constancia:

- Los **JWT vencieron el 09/11/2026 a las 22:18**, más de 24 horas antes de archivarse. No eran
  utilizables ya al momento de preservar la evidencia.
- La **contraseña temporal** y las **contraseñas de prueba** pertenecen a cuentas ficticias de una
  base local (`turisclick_staging_o19`) que se va a borrar. No existen en ningún otro entorno.
- La **semilla de la clave JWT de staging** servía sólo para ese entorno local. Lo que importa
  documentar es el método —cada ambiente con su propia clave de firma, que es lo que demuestra
  S-2—, no el literal.

Lo que **sí se conservó** porque es evidencia y no secreto: identificadores de recursos (paquete,
reserva, empresa, cotizaciones), `traceId`, rutas de API, códigos de estado HTTP, cuerpos de
respuesta y los correos de las cuentas ficticias (`admin@turisclick.dev`,
`qa.operador.276185@turisclick.dev`, `qa.turista.276185@turisclick.dev`), que no corresponden a
personas reales.

---

## Ausencias

No falta ningún archivo: los cinco que respaldan la batería automática están presentes. Lo único
sin respaldo documental propio son las pruebas manuales, por lo dicho arriba — y eso está
declarado, no reconstruido.
