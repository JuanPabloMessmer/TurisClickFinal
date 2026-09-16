# Scripts de bootstrap y operaciones

Operaciones que, a propósito, no hace Terraform (ver `../README.md`). Windows PowerShell 5.1 con `az login`. Todos son idempotentes.

| Script | Qué hace | Estado |
|---|---|---|
| `register-providers.ps1` | Registra `Microsoft.KeyVault` y `Microsoft.Storage` | ✅ ejecutado |
| `bootstrap-state.ps1` | Backend del state: RG, storage sin shared keys, versionado, soft delete, container, rol de datos, lock | ✅ ejecutado |
| `set-keyvault-secrets.ps1` | Genera y carga los secretos indicados con `-Secret` (`jwt-key`, `seed-admin-password`). Sin `-Secret` no hace nada | ✅ `jwt-key` · ⏸ `seed-admin-password` |
| `create-db-role.ps1` | Crea el rol `turisclick_v2_app`, lo hace dueño de `turisclick_db_v2` y de su schema `public`, y carga `db-connection-string` | ✅ ejecutado |
| `run-migrations.ps1` | Aplica las migraciones EF Core de V2 sobre `turisclick_db_v2` | ⏸ no ejecutado |
| `run-seed.ps1` | Seed único de datos de demo sobre `turisclick_db_v2` | ⏸ no ejecutado |
| `lock-postgres.ps1` | Lock `CanNotDelete` sobre el servidor PostgreSQL | ⏸ no ejecutado |
| `common.ps1` | Funciones compartidas (no se ejecuta solo) | — |

## Garantías que comparten

- **Secretos**: se generan en memoria y viajan a Key Vault por REST y a PostgreSQL por stdin y `PGPASSWORD`. Nunca se imprimen, ni pasan por argumentos, ni se escriben a disco. La contraseña de PostgreSQL se envía como verificador SCRAM.
- **Base V1**: `Invoke-PsqlStdin` y `Assert-V2ConnectionString` abortan si el destino es `turisclick_db`.
- **Firewall**: `Invoke-WithTemporaryFirewallRule` abre sólo la IP actual, cierra en un `finally` y verifica que el firewall quede idéntico. Se niega a empezar si el servidor tiene un lock, porque no podría cerrar la regla.

## Uso

```powershell
.\create-db-role.ps1                         # idempotente; no cambia la contraseña si el secreto ya existe
.\create-db-role.ps1 -RotatePassword         # genera una contraseña nueva y actualiza el secreto
.\set-keyvault-secrets.ps1 -Secret jwt-key   # sólo los secretos indicados; -Rotate para regenerar
```

`create-db-role.ps1 -SkipTemporaryFirewall` y `run-seed.ps1 -SkipTemporaryFirewall` no abren ni cierran reglas: sirven para encadenar varias operaciones dentro de una única ventana abierta con `Invoke-WithTemporaryFirewallRule`.

## PostgreSQL en Azure Flexible Server

- El schema `public` de una base nueva pertenece a `azure_pg_admin`, no al dueño de la base. Ser dueño de `turisclick_db_v2` no alcanza para crear tablas: `create-db-role.ps1` transfiere también el schema.
- Sobre `turisclick_db` (V1) no se otorga ni se revoca nada. Sus permisos por defecto dan `CONNECT` y `TEMP` a `PUBLIC`, así que `turisclick_v2_app` puede conectarse, pero no puede leer, escribir ni crear objetos. Es un riesgo aceptado: revocarlo modificaría la base V1.

## Notas de PowerShell 5.1 (aprendidas en la práctica)

- Los `.ps1` se guardan en UTF-8 **con BOM**: sin él, 5.1 los lee como ANSI.
- `az.cmd` pasa por `cmd.exe`: nada de paréntesis en `--query` (por ejemplo `length(@)`); se usa `-o tsv`.
- `ConvertFrom-Json` de `[]` devuelve un único objeto: contar su resultado da 1.
- `DbConnectionStringBuilder` necesita `.psbase.ConnectionString`.
- Canalizar texto hacia un ejecutable nativo antepone un BOM: no hashear así.
