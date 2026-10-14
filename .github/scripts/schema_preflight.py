#!/usr/bin/env python3
"""
Verificación de esquema del deploy de TurisClick V2 (backend-v2.yml).

Hasta la Oleada 15 el workflow decidía si la base estaba al día buscando "does not exist" en el cuerpo
de un 500: dependía de que el backend publicara el error crudo de PostgreSQL, que era justamente un
agujero de seguridad. Cerrado el agujero, esa verificación habría dejado pasar cualquier base atrasada.

Ahora el pipeline calcula la huella del esquema que necesita el commit a desplegar —SHA-256 de
"turisclick_db_v2" y de sus migraciones, en orden— y se la manda a GET /api/health?expect=…. La app la
compara con la base real (current_database() y __EFMigrationsHistory) y responde sólo verdadero o falso:
la respuesta pública nunca nombra la base ni las migraciones.

  pre   antes de desplegar: la base es turisclick_db_v2 con exactamente las migraciones del build.
  post  después de desplegar: lo mismo, contra el código nuevo, que además tiene que estar sano.
        Obligatoria: corre aunque fallen los smoke tests, y su falla hace fallar el workflow.

En las dos fases, además: la URL es la de la app V2, la app apunta por configuración al secreto V2 de Key
Vault, y cualquier 500 en las sondas de negocio hace fallar el paso. Nunca se lee el cuerpo de un error.

Excepción de primer deploy (FIRST_DEPLOY): la app que corre hoy es anterior a /api/health. Ver
check_first_deploy: está atada a ese paquete exacto y a las 14 migraciones de hoy, así que deja de
aplicar sola en cuanto cambie cualquiera de las dos cosas.

Sólo biblioteca estándar. Las decisiones son funciones puras (test_schema_preflight.py); la red y los
argumentos están en main().
"""
from __future__ import annotations

import argparse
import hashlib
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
EXPECTED_HOST = "app-turisclick-v2-api.azurewebsites.net"
EXPECTED_CONNECTION_REFERENCE = (
    "@Microsoft.KeyVault(VaultName=kv-turisclick-v2-dev;SecretName=db-connection-string)"
)

# La única situación en la que se acepta no poder verificar la base desde la app. Es el paquete que corre
# en app-turisclick-v2-api desde el 7 de octubre de 2026 (build de e8d0756, anterior a /api/health), contra
# la base que la Oleada 15 dejó en 0014. Guardado y ensayado como rollback: docs/wave15-readiness.md.
FIRST_DEPLOY = {
    "active_deployment_id": "e66e32a0-6106-469e-b9d3-e55d9cbc8ab9",
    "latest_migration": "20261007024736_0014_Oleada13_AltaDeOperadoresPorAdmin",
    "migration_count": 14,
}

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
    warnings: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not self.errors


@dataclass
class Response:
    status: int  # 0 = sin respuesta
    body: str = ""


@dataclass
class FirstDeployRequest:
    """Lo que la persona que lanza el workflow declara para usar la excepción de primer deploy."""

    requested: bool = False
    migrations_applied: str | None = None
    active_deployment_id: str | None = None


# ------------------------------------------------------------------ decisiones (puras)


def expected_migrations(migrations_dir: Path) -> list[str]:
    """Migraciones que el código declara, en el orden en que EF las aplica (orden del id)."""
    ids = []
    for path in migrations_dir.glob("*.Designer.cs"):
        ids.extend(MIGRATION_ATTRIBUTE.findall(path.read_text(encoding="utf-8-sig")))
    if not ids:
        raise ValueError(f"No se encontraron migraciones en {migrations_dir}")
    return sorted(ids)


def fingerprint(database: str, migrations: list[str]) -> str:
    """Igual que DatabaseSchemaInspector.Fingerprint: SHA-256 de la base y las migraciones, una por línea."""
    return hashlib.sha256("\n".join([database, *migrations]).encode("utf-8")).hexdigest()


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


def check_health(response: Response, expected: list[str], phase: str, first_deploy: FirstDeployRequest) -> Result:
    health = parse_health(response)

    if health is None:
        if phase == "pre" and response.status == 404:
            return check_first_deploy(expected, first_deploy)
        result = Result()
        result.errors.append(f"/api/health respondió {response.status or 'sin respuesta'}: no se puede verificar la base.")
        return result

    result = Result()
    if first_deploy.requested:
        # La excepción no se usa "por las dudas": si la app ya puede informar, se desmarca.
        result.errors.append(
            "Se pidió first_deploy_bootstrap pero la app ya tiene /api/health. Relanzar sin esa opción."
        )
        return result

    if health.get("schemaMatches") is not True:
        result.errors.append(
            f"La base de la app no es {EXPECTED_DATABASE} con exactamente las {len(expected)} migraciones del "
            f"código (última '{expected[-1]}'): puede faltar o sobrar una migración, o ser otra base. El detalle "
            "está en GET /api/admin/health/schema (ADMIN). No se despliega."
        )
        return result

    if health.get("status") != "HEALTHY" or response.status != 200:
        result.errors.append(f"La app no está sana (estado {health.get('status')}, HTTP {response.status}).")
        return result

    result.notes.append(f"{EXPECTED_DATABASE} tiene exactamente las {len(expected)} migraciones del código.")
    return result


def check_first_deploy(expected: list[str], request: FirstDeployRequest) -> Result:
    """
    La app desplegada no tiene /api/health. Se acepta, sólo en la fase pre, si se cumple TODO:

      1. se pidió explícitamente (input first_deploy_bootstrap);
      2. la app que corre es exactamente el paquete pre-Oleada 15 conocido (id de deployment activo);
      3. el código a desplegar tiene exactamente las 14 migraciones de hoy (agregar una apaga la excepción);
      4. quien lanza declara el id COMPLETO de la última migración, verificada por fuera del pipeline.

    La identidad de la base sigue cubierta por check_target, y la fase post la verifica de verdad.
    """
    result = Result()
    if not request.requested:
        result.errors.append(
            "La app desplegada no tiene /api/health y no se pidió first_deploy_bootstrap: no se puede verificar la base."
        )
    if request.active_deployment_id != FIRST_DEPLOY["active_deployment_id"]:
        result.errors.append(
            f"El deployment activo es '{request.active_deployment_id or 'desconocido'}', no el paquete pre-Oleada 15 "
            f"('{FIRST_DEPLOY['active_deployment_id']}'). La excepción de primer deploy no aplica."
        )
    if expected[-1] != FIRST_DEPLOY["latest_migration"] or len(expected) != FIRST_DEPLOY["migration_count"]:
        result.errors.append(
            f"El código tiene {len(expected)} migraciones (última '{expected[-1]}'); la excepción de primer deploy "
            f"sólo vale con {FIRST_DEPLOY['migration_count']} (última '{FIRST_DEPLOY['latest_migration']}')."
        )
    if (request.migrations_applied or "").strip() != expected[-1]:
        result.errors.append(f"'migrations_applied' tiene que ser el id completo '{expected[-1]}'.")

    if result.ok:
        result.warnings.append(
            "Primer deploy: la app desplegada no tiene /api/health; se aceptó la migración declarada. "
            "La fase post verifica la base de verdad."
        )
    return result


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
        merged.warnings.extend(r.warnings)
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
                        help="JSON de 'az webapp config appsettings list' (sólo se lee la connection string)")
    parser.add_argument("--first-deploy-bootstrap", action="store_true")
    parser.add_argument("--migrations-applied", default=None)
    parser.add_argument("--active-deployment-id", default=None)
    args = parser.parse_args(argv)

    if args.phase == "post" and args.first_deploy_bootstrap:
        print("::error title=Verificación de esquema (post)::La excepción de primer deploy no existe después de desplegar.")
        return 1

    expected = expected_migrations(args.migrations_dir)
    settings = json.loads(args.app_settings.read_text(encoding="utf-8"))
    connection = next((s.get("value") for s in settings if s.get("name") == "ConnectionStrings__DefaultConnection"), None)
    first_deploy = FirstDeployRequest(args.first_deploy_bootstrap, args.migrations_applied, args.active_deployment_id)

    base = args.app_url.rstrip("/")
    results = [check_target(base, connection)]
    if results[0].ok:
        health_url = f"{base}/api/health?expect={fingerprint(EXPECTED_DATABASE, expected)}"
        results.append(check_health(fetch(health_url), expected, args.phase, first_deploy))
        for name, method, path, payload, allowed in PROBES:
            results.append(check_probe(name, fetch(f"{base}{path}", method, payload), allowed))

    result = merge(*results)
    for note in result.notes:
        print(f"  {note}")
    for warning in result.warnings:
        print(f"::warning title=Verificación de esquema ({args.phase})::{warning}")
    for error in result.errors:
        print(f"::error title=Verificación de esquema ({args.phase})::{error}")
    if result.ok and not result.warnings:
        print(f"::notice title=Esquema ({args.phase})::{EXPECTED_DATABASE} tiene las {len(expected)} migraciones del código")
    return 0 if result.ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
