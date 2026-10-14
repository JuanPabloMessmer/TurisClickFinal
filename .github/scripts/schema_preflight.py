#!/usr/bin/env python3
"""
Verificación de esquema del deploy de TurisClick V2 (backend-v2.yml).

Hasta la Oleada 15 el workflow decidía si la base estaba al día buscando "does not exist" en el cuerpo
de un 500: dependía de que el backend publicara el error crudo de PostgreSQL, que era justamente un
agujero de seguridad. Cerrado el agujero, esa verificación habría dejado pasar cualquier base atrasada.

Ahora se compara lo que el código a desplegar necesita (sus migraciones, leídas del código fuente) con lo
que la base tiene (lo informa GET /api/health, que consulta current_database() y __EFMigrationsHistory).

  pre   antes de desplegar: la base es turisclick_db_v2 y tiene exactamente las migraciones del build.
  post  después de desplegar: lo mismo, y el código nuevo no ve ninguna migración pendiente.

Además, en las dos fases: la URL es la de la app V2, la app apunta por configuración al secreto V2 de Key
Vault, y cualquier 500 en las sondas de negocio hace fallar el paso, diga lo que diga el cuerpo.

Sólo usa la biblioteca estándar. Las decisiones son funciones puras (ver test_schema_preflight.py); la
red y los argumentos están en main().
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from urllib.parse import urlparse

EXPECTED_DATABASE = "turisclick_db_v2"
V1_DATABASE = "turisclick_db"
EXPECTED_HOST = "app-turisclick-v2-api.azurewebsites.net"
EXPECTED_CONNECTION_REFERENCE = (
    "@Microsoft.KeyVault(VaultName=kv-turisclick-v2-dev;SecretName=db-connection-string)"
)

# Sondas de negocio: lo único aceptable es la respuesta normal. Un 500 nunca pasa.
PROBES = (
    ("GET /api/packages", "GET", "/api/packages?pageSize=1", None, {200}),
    (
        "POST /api/auth/login",
        "POST",
        "/api/auth/login",
        {"email": "schema-probe@invalid.local", "password": "NoEsUnaCredencial1"},
        {401},
    ),
)

MIGRATION_ATTRIBUTE = re.compile(r'\[Migration\("([^"]+)"\)\]')


@dataclass
class Result:
    errors: list[str] = field(default_factory=list)
    notes: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not self.errors


@dataclass
class Response:
    status: int  # 0 = sin respuesta
    body: str = ""


# ------------------------------------------------------------------ decisiones (puras)


def expected_migrations(migrations_dir: Path) -> list[str]:
    """Migraciones que el código declara, en el orden en que EF las aplica (orden del id)."""
    ids = []
    for path in migrations_dir.glob("*.Designer.cs"):
        ids.extend(MIGRATION_ATTRIBUTE.findall(path.read_text(encoding="utf-8-sig")))
    if not ids:
        raise ValueError(f"No se encontraron migraciones en {migrations_dir}")
    return sorted(ids)


def check_target(app_url: str, connection_setting: str | None) -> Result:
    """La URL es la app V2 y su connection string es la referencia al secreto V2 (no un valor en claro)."""
    result = Result()
    host = urlparse(app_url).hostname
    if host != EXPECTED_HOST:
        result.errors.append(f"La URL apunta a '{host}', se esperaba '{EXPECTED_HOST}'.")
    if connection_setting is None:
        result.errors.append("No se pudo leer ConnectionStrings__DefaultConnection de la app.")
    elif connection_setting != EXPECTED_CONNECTION_REFERENCE:
        # No se imprime el valor: si no es la referencia esperada, podría ser una connection string en claro.
        result.errors.append(
            "ConnectionStrings__DefaultConnection no es la referencia al secreto V2 de Key Vault "
            "(el valor no se muestra)."
        )
    else:
        result.notes.append("La app usa el secreto db-connection-string de kv-turisclick-v2-dev.")
    return result


def parse_health(response: Response) -> dict | None:
    """El cuerpo de /api/health, o None si la respuesta no es de ese endpoint (p. ej. un 503 de Azure)."""
    if response.status not in (200, 503):
        return None
    try:
        body = json.loads(response.body)
    except ValueError:
        return None
    return body if isinstance(body, dict) and "status" in body else None


def check_health(response: Response, expected: list[str], phase: str, migrations_applied: str | None) -> Result:
    result = Result()
    health = parse_health(response)

    if health is None:
        if phase == "pre" and response.status == 404:
            return check_bootstrap(expected, migrations_applied)
        result.errors.append(f"/api/health respondió {response.status or 'sin respuesta'}: no se puede verificar la base.")
        return result

    status = health.get("status")
    database = health.get("database")
    latest = health.get("latestMigration")
    applied = health.get("appliedMigrations")
    pending = health.get("pendingMigrations")

    if status == "DATABASE_UNAVAILABLE":
        result.errors.append("La app no puede consultar su base.")
        return result

    if database == V1_DATABASE:
        result.errors.append("ABORTADO: la app está conectada a turisclick_db, la base de V1.")
        return result
    if database != EXPECTED_DATABASE:
        result.errors.append(f"La app está conectada a '{database}', se esperaba '{EXPECTED_DATABASE}'.")
        return result

    if latest != expected[-1] or applied != len(expected):
        if isinstance(applied, int) and applied < len(expected):
            missing = len(expected) - applied
            result.errors.append(
                f"Faltan {missing} migración(es) en {EXPECTED_DATABASE}: la base llega a '{latest}' y el código "
                f"necesita '{expected[-1]}'. Aplicarlas antes de desplegar (docs/azure-v2-deployment-plan.md)."
            )
        else:
            result.errors.append(
                f"La base ({applied} migraciones, última '{latest}') no coincide con el código "
                f"({len(expected)}, última '{expected[-1]}'). ¿Se está desplegando un commit viejo?"
            )
        return result

    if phase == "post" and (pending != 0 or status != "HEALTHY" or response.status != 200):
        result.errors.append(f"El código desplegado ve {pending} migración(es) pendiente(s) (estado {status}).")
        return result

    result.notes.append(f"{EXPECTED_DATABASE}: {applied} migraciones, última '{latest}'.")
    return result


def check_bootstrap(expected: list[str], migrations_applied: str | None) -> Result:
    """
    La app desplegada todavía no tiene /api/health (es anterior a la Oleada 15). Por única vez, la
    verificación de la base la hace la persona que lanza el workflow, que declara la migración aplicada.
    La identidad de la base sigue cubierta por check_target y la verificación post-deploy la confirma.
    """
    result = Result()
    latest = expected[-1]
    declared = (migrations_applied or "").strip()
    if not declared:
        result.errors.append(
            "La app desplegada no tiene /api/health y no se declaró 'migrations_applied'. "
            f"Declararla (ej. '{migration_number(latest)}') después de verificar el historial de la base."
        )
    elif declared != latest and declared != migration_number(latest):
        result.errors.append(f"'migrations_applied' es '{declared}', pero el código necesita '{latest}'.")
    else:
        result.notes.append(
            "AVISO: la app desplegada no tiene /api/health. Se acepta la migración declarada "
            f"('{declared}'); la fase post verifica la base de verdad."
        )
    return result


def migration_number(migration_id: str) -> str:
    """'20261007024736_0014_Oleada13_...' -> '0014'."""
    parts = migration_id.split("_")
    return parts[1] if len(parts) > 1 else migration_id


def check_probe(name: str, response: Response, allowed: set[int]) -> Result:
    result = Result()
    if response.status in allowed:
        result.notes.append(f"{name} -> {response.status}")
    elif response.status >= 500:
        result.errors.append(f"{name} -> {response.status}: error del servidor.")
    else:
        result.errors.append(f"{name} -> {response.status or 'sin respuesta'}, se esperaba {sorted(allowed)}.")
    return result


def merge(*results: Result) -> Result:
    merged = Result()
    for r in results:
        merged.errors.extend(r.errors)
        merged.notes.extend(r.notes)
    return merged


# ------------------------------------------------------------------ red


def is_waking_up(response: Response) -> bool:
    """F1 no tiene Always On: un 502/503/504 sin el cuerpo de /api/health es la app arrancando."""
    if response.status == 0 or response.status in (502, 504):
        return True
    return response.status == 503 and parse_health(response) is None


def fetch(url: str, method: str = "GET", payload: dict | None = None, attempts: int = 12, wait: float = 15) -> Response:
    data = json.dumps(payload).encode() if payload is not None else None
    response = Response(0)
    for attempt in range(1, attempts + 1):
        request = urllib.request.Request(url, data=data, method=method, headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(request, timeout=90) as r:
                response = Response(r.status, r.read().decode("utf-8", "replace"))
        except urllib.error.HTTPError as e:
            response = Response(e.code, e.read().decode("utf-8", "replace"))
        except (urllib.error.URLError, TimeoutError):
            response = Response(0)
        if not is_waking_up(response) or attempt == attempts:
            return response
        print(f"  {method} {urlparse(url).path} -> {response.status or 'sin respuesta'} (intento {attempt}), reintentando...")
        time.sleep(wait)
    return response


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("phase", choices=["pre", "post"])
    parser.add_argument("--app-url", required=True)
    parser.add_argument("--migrations-dir", type=Path, required=True)
    parser.add_argument("--app-settings", type=Path, required=True,
                        help="JSON de 'az webapp config appsettings list' (sólo se lee el nombre de la base)")
    parser.add_argument("--migrations-applied", default=None)
    args = parser.parse_args(argv)

    expected = expected_migrations(args.migrations_dir)
    settings = json.loads(args.app_settings.read_text(encoding="utf-8"))
    connection = next((s.get("value") for s in settings if s.get("name") == "ConnectionStrings__DefaultConnection"), None)

    base = args.app_url.rstrip("/")
    results = [check_target(base, connection)]
    if results[0].ok:
        results.append(check_health(fetch(f"{base}/api/health"), expected, args.phase, args.migrations_applied))
        for name, method, path, payload, allowed in PROBES:
            results.append(check_probe(name, fetch(f"{base}{path}", method, payload), allowed))

    result = merge(*results)
    for note in result.notes:
        print(f"  {note}")
    for error in result.errors:
        print(f"::error title=Verificación de esquema ({args.phase})::{error}")
    if result.ok and any(note.startswith("AVISO") for note in result.notes):
        print(f"::warning title=Esquema ({args.phase})::migración declarada a mano, sin verificar desde la app")
    elif result.ok:
        print(f"::notice title=Esquema ({args.phase})::{EXPECTED_DATABASE} tiene las {len(expected)} migraciones del código")
    return 0 if result.ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
