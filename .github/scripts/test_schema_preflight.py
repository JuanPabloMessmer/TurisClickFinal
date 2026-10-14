"""Pruebas de schema_preflight.py. Correr con: python -m unittest discover -s .github/scripts -v"""
import json
import unittest
from pathlib import Path

from schema_preflight import (
    EXPECTED_CONNECTION_REFERENCE,
    Response,
    check_health,
    check_probe,
    check_target,
    expected_migrations,
    is_waking_up,
)

REPO = Path(__file__).resolve().parents[2]
MIGRATIONS_DIR = REPO / "src" / "TurisClick.Api" / "Infrastructure" / "Database" / "Migrations"

EXPECTED = [
    "20260917011152_0009_Oleada9_DestinationImagesAndTouristPreferences",
    "20261005110805_0010_Oleada10_VuelosEnPaquetes",
    "20261007024736_0014_Oleada13_AltaDeOperadoresPorAdmin",
]
APP_URL = "https://app-turisclick-v2-api.azurewebsites.net"


def health(http=200, **overrides):
    body = {
        "status": "HEALTHY",
        "database": "turisclick_db_v2",
        "appliedMigrations": len(EXPECTED),
        "latestMigration": EXPECTED[-1],
        "pendingMigrations": 0,
    }
    body.update(overrides)
    return Response(http, json.dumps(body))


class ExpectedMigrationsTests(unittest.TestCase):
    def test_lee_las_migraciones_del_codigo_en_orden(self):
        ids = expected_migrations(MIGRATIONS_DIR)
        self.assertEqual(ids, sorted(ids))
        self.assertTrue(ids[0].endswith("_0001_Oleada0_Auth"))
        # Una por archivo de migración (sin contar el snapshot).
        files = [p for p in MIGRATIONS_DIR.glob("*.cs") if not p.name.endswith(".Designer.cs") and "Snapshot" not in p.name]
        self.assertEqual(len(ids), len(files))


class TargetTests(unittest.TestCase):
    def test_acepta_la_app_v2_con_la_referencia_v2(self):
        self.assertTrue(check_target(APP_URL, EXPECTED_CONNECTION_REFERENCE).ok)

    def test_rechaza_otra_app(self):
        result = check_target("https://turisclick-api-abd8gef5hfhxg2hr.eastus2-01.azurewebsites.net", EXPECTED_CONNECTION_REFERENCE)
        self.assertFalse(result.ok)

    def test_rechaza_una_connection_string_en_claro_sin_mostrarla(self):
        secret = "Host=x;Database=turisclick_db;Username=u;Password=hunter2"
        result = check_target(APP_URL, secret)
        self.assertFalse(result.ok)
        self.assertNotIn("hunter2", " ".join(result.errors))

    def test_rechaza_la_referencia_a_otro_secreto(self):
        other = EXPECTED_CONNECTION_REFERENCE.replace("db-connection-string", "v1-connection-string")
        self.assertFalse(check_target(APP_URL, other).ok)

    def test_rechaza_si_no_hay_connection_string(self):
        self.assertFalse(check_target(APP_URL, None).ok)


class HealthTests(unittest.TestCase):
    def test_base_al_dia_pasa_en_las_dos_fases(self):
        self.assertTrue(check_health(health(), EXPECTED, "pre", None).ok)
        self.assertTrue(check_health(health(), EXPECTED, "post", None).ok)

    def test_base_atrasada_falla_antes_de_desplegar(self):
        result = check_health(health(appliedMigrations=2, latestMigration=EXPECTED[1]), EXPECTED, "pre", None)
        self.assertFalse(result.ok)
        self.assertIn("Faltan 1", result.errors[0])

    def test_base_atrasada_falla_aunque_se_declare_la_migracion(self):
        # La declaración manual sólo vale cuando la app no puede informar; si informa, manda la base.
        result = check_health(health(appliedMigrations=2, latestMigration=EXPECTED[1]), EXPECTED, "pre", "0014")
        self.assertFalse(result.ok)

    def test_base_adelantada_falla(self):
        result = check_health(health(appliedMigrations=4, latestMigration="20261101000000_0015_X"), EXPECTED, "pre", None)
        self.assertFalse(result.ok)

    def test_la_base_v1_aborta(self):
        result = check_health(health(database="turisclick_db"), EXPECTED, "pre", None)
        self.assertFalse(result.ok)
        self.assertIn("V1", result.errors[0])

    def test_otra_base_falla(self):
        self.assertFalse(check_health(health(database="turisclick_v2_dev"), EXPECTED, "pre", None).ok)

    def test_base_inalcanzable_falla(self):
        response = Response(503, json.dumps({"status": "DATABASE_UNAVAILABLE", "database": None}))
        self.assertFalse(check_health(response, EXPECTED, "pre", None).ok)

    def test_post_falla_si_el_codigo_ve_migraciones_pendientes(self):
        response = health(503, status="PENDING_MIGRATIONS", pendingMigrations=1)
        self.assertFalse(check_health(response, EXPECTED, "post", None).ok)

    def test_un_500_de_health_falla(self):
        self.assertFalse(check_health(Response(500, '{"title":"Error interno"}'), EXPECTED, "pre", None).ok)

    def test_un_500_con_texto_de_postgres_tambien_falla(self):
        # Antes se miraba si el cuerpo decía "does not exist". Ya no se lee el cuerpo de un 500.
        response = Response(500, '{"detail":"42703: column x does not exist"}')
        self.assertFalse(check_health(response, EXPECTED, "pre", None).ok)

    def test_un_503_html_de_azure_no_se_confunde_con_health(self):
        self.assertFalse(check_health(Response(503, "<html>Service Unavailable</html>"), EXPECTED, "pre", None).ok)


class BootstrapTests(unittest.TestCase):
    """La app desplegada hoy es anterior a /api/health: 404 en la fase pre."""

    def test_sin_declaracion_falla(self):
        self.assertFalse(check_health(Response(404), EXPECTED, "pre", None).ok)

    def test_declarando_la_ultima_migracion_pasa_con_aviso(self):
        for declared in ("0014", EXPECTED[-1]):
            result = check_health(Response(404), EXPECTED, "pre", declared)
            self.assertTrue(result.ok, declared)
            self.assertIn("AVISO", result.notes[0])

    def test_declarando_una_migracion_vieja_falla(self):
        self.assertFalse(check_health(Response(404), EXPECTED, "pre", "0013").ok)

    def test_en_post_un_404_falla_siempre(self):
        # Después de desplegar, el código nuevo tiene que tener el endpoint.
        self.assertFalse(check_health(Response(404), EXPECTED, "post", "0014").ok)


class ProbeTests(unittest.TestCase):
    def test_respuesta_esperada_pasa(self):
        self.assertTrue(check_probe("login", Response(401), {401}).ok)

    def test_cualquier_500_falla(self):
        for body in ("", '{"detail":"Ocurrió un error inesperado."}', '{"detail":"does not exist"}'):
            self.assertFalse(check_probe("packages", Response(500, body), {200}).ok)

    def test_un_200_donde_se_esperaba_401_falla(self):
        self.assertFalse(check_probe("login", Response(200), {401}).ok)

    def test_sin_respuesta_falla(self):
        self.assertFalse(check_probe("packages", Response(0), {200}).ok)


class WakeUpTests(unittest.TestCase):
    def test_reintenta_mientras_la_app_arranca(self):
        for response in (Response(0), Response(502), Response(504), Response(503, "<html/>")):
            self.assertTrue(is_waking_up(response))

    def test_no_reintenta_respuestas_reales(self):
        for response in (Response(200), Response(500), Response(404), health(503, status="DATABASE_UNAVAILABLE")):
            self.assertFalse(is_waking_up(response))


if __name__ == "__main__":
    unittest.main()
