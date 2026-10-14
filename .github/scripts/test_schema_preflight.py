"""Pruebas de schema_preflight.py y de cómo lo usa backend-v2.yml.

Correr con: python -m unittest discover -s .github/scripts -v
"""
import json
import re
import tempfile
import unittest
from pathlib import Path

import schema_preflight
from schema_preflight import (
    EXPECTED_CONNECTION_REFERENCE,
    FIRST_DEPLOY,
    FirstDeployRequest,
    Response,
    check_health,
    check_probe,
    check_target,
    expected_migrations,
    fingerprint,
    is_waking_up,
    main,
)

REPO = Path(__file__).resolve().parents[2]
MIGRATIONS_DIR = REPO / "src" / "TurisClick.Api" / "Infrastructure" / "Database" / "Migrations"
WORKFLOW = REPO / ".github" / "workflows" / "backend-v2.yml"

# Las 14 migraciones de hoy: las que acepta la excepción de primer deploy.
TODAY = [f"2026{i:010d}_{i:04d}_M" for i in range(1, 14)] + [FIRST_DEPLOY["latest_migration"]]
APP_URL = "https://app-turisclick-v2-api.azurewebsites.net"
NO_BOOTSTRAP = FirstDeployRequest()
VALID_BOOTSTRAP = FirstDeployRequest(
    requested=True,
    migrations_applied=FIRST_DEPLOY["latest_migration"],
    active_deployment_id=FIRST_DEPLOY["active_deployment_id"],
)


def health(http=200, status="HEALTHY", schema_matches=True):
    return Response(http, json.dumps({"status": status, "schemaMatches": schema_matches}))


class FingerprintTests(unittest.TestCase):
    def test_coincide_con_el_vector_compartido_con_el_backend(self):
        # El mismo vector está en tests/TurisClick.Api.Tests/Infrastructure/SchemaFingerprintTests.cs.
        self.assertEqual(
            fingerprint("turisclick_db_v2", [
                "20260812013133_0001_Oleada0_Auth",
                "20261007024736_0014_Oleada13_AltaDeOperadoresPorAdmin",
            ]),
            "f3e4276d553ede3d14d2f599f0bfbdae90fd83a5e8c89a738869cf86fe0708e5",
        )

    def test_la_base_v1_da_otra_huella(self):
        self.assertNotEqual(fingerprint("turisclick_db", TODAY), fingerprint("turisclick_db_v2", TODAY))


class ExpectedMigrationsTests(unittest.TestCase):
    def test_lee_las_migraciones_del_codigo_en_orden(self):
        ids = expected_migrations(MIGRATIONS_DIR)
        self.assertEqual(ids, sorted(ids))
        self.assertTrue(ids[0].endswith("_0001_Oleada0_Auth"))
        files = [p for p in MIGRATIONS_DIR.glob("*.cs") if not p.name.endswith(".Designer.cs") and "Snapshot" not in p.name]
        self.assertEqual(len(ids), len(files))


class TargetTests(unittest.TestCase):
    def test_acepta_la_app_v2_con_la_referencia_v2(self):
        self.assertTrue(check_target(APP_URL, EXPECTED_CONNECTION_REFERENCE).ok)

    def test_rechaza_otra_app(self):
        url = "https://turisclick-api-abd8gef5hfhxg2hr.eastus2-01.azurewebsites.net"
        self.assertFalse(check_target(url, EXPECTED_CONNECTION_REFERENCE).ok)

    def test_rechaza_una_connection_string_en_claro_sin_mostrarla(self):
        result = check_target(APP_URL, "Host=x;Database=turisclick_db;Username=u;Password=hunter2")
        self.assertFalse(result.ok)
        self.assertNotIn("hunter2", " ".join(result.errors))

    def test_rechaza_la_referencia_a_otro_secreto(self):
        other = EXPECTED_CONNECTION_REFERENCE.replace("db-connection-string", "v1-connection-string")
        self.assertFalse(check_target(APP_URL, other).ok)

    def test_rechaza_si_no_hay_connection_string(self):
        self.assertFalse(check_target(APP_URL, None).ok)


class HealthTests(unittest.TestCase):
    def test_huella_que_coincide_y_app_sana_pasa_en_las_dos_fases(self):
        for phase in ("pre", "post"):
            self.assertTrue(check_health(health(), TODAY, phase, NO_BOOTSTRAP).ok, phase)

    def test_huella_que_no_coincide_falla(self):
        # Otra base (V1 incluida) o migraciones de más o de menos: la app sólo dice que no coincide.
        for phase in ("pre", "post"):
            self.assertFalse(check_health(health(schema_matches=False), TODAY, phase, NO_BOOTSTRAP).ok, phase)

    def test_sin_respuesta_a_la_huella_falla(self):
        response = Response(200, json.dumps({"status": "HEALTHY"}))
        self.assertFalse(check_health(response, TODAY, "pre", NO_BOOTSTRAP).ok)

    def test_app_no_sana_falla(self):
        self.assertFalse(check_health(health(503, "UNHEALTHY", True), TODAY, "post", NO_BOOTSTRAP).ok)
        self.assertFalse(check_health(health(503, "UNHEALTHY", False), TODAY, "pre", NO_BOOTSTRAP).ok)

    def test_un_500_de_health_falla_lea_lo_que_lea(self):
        for body in ("", '{"title":"Error interno"}', '{"detail":"42703: column x does not exist"}'):
            self.assertFalse(check_health(Response(500, body), TODAY, "pre", NO_BOOTSTRAP).ok)

    def test_un_503_html_de_azure_no_se_confunde_con_health(self):
        self.assertFalse(check_health(Response(503, "<html>Service Unavailable</html>"), TODAY, "pre", NO_BOOTSTRAP).ok)


class FirstDeployTests(unittest.TestCase):
    """La app desplegada hoy es anterior a /api/health: responde 404."""

    def test_con_todas_las_condiciones_pasa_con_advertencia(self):
        result = check_health(Response(404), TODAY, "pre", VALID_BOOTSTRAP)
        self.assertTrue(result.ok, result.errors)
        self.assertTrue(result.warnings)

    def test_sin_pedirla_falla(self):
        request = FirstDeployRequest(False, VALID_BOOTSTRAP.migrations_applied, VALID_BOOTSTRAP.active_deployment_id)
        self.assertFalse(check_health(Response(404), TODAY, "pre", request).ok)

    def test_con_otro_paquete_desplegado_falla(self):
        # Por ejemplo después de un rollback a otro paquete, o si alguien desplegó a mano.
        for deployment in ("otro-id", "", None):
            request = FirstDeployRequest(True, VALID_BOOTSTRAP.migrations_applied, deployment)
            self.assertFalse(check_health(Response(404), TODAY, "pre", request).ok, deployment)

    def test_con_una_migracion_nueva_en_el_codigo_ya_no_aplica(self):
        code = TODAY + ["20261101000000_0015_Nueva"]
        result = check_health(Response(404), code, "pre", VALID_BOOTSTRAP)
        self.assertFalse(result.ok)
        self.assertTrue(any("primer deploy" in e for e in result.errors))

    def test_con_migraciones_de_menos_en_el_codigo_no_aplica(self):
        self.assertFalse(check_health(Response(404), TODAY[:-1], "pre", VALID_BOOTSTRAP).ok)

    def test_declarar_el_numero_corto_no_alcanza(self):
        for declared in ("0014", "14", "", None, "20261007015504_0013_Oleada12_ReembolsoExitosoUnico"):
            request = FirstDeployRequest(True, declared, VALID_BOOTSTRAP.active_deployment_id)
            self.assertFalse(check_health(Response(404), TODAY, "pre", request).ok, declared)

    def test_pedirla_cuando_la_app_ya_tiene_health_falla(self):
        # Aunque la base esté perfecta: la excepción no se usa "por las dudas".
        self.assertFalse(check_health(health(), TODAY, "pre", VALID_BOOTSTRAP).ok)

    def test_nunca_aplica_despues_de_desplegar(self):
        self.assertFalse(check_health(Response(404), TODAY, "post", VALID_BOOTSTRAP).ok)

    def test_solo_un_404_la_habilita(self):
        for response in (Response(500), Response(0), Response(401), Response(503, "<html/>")):
            self.assertFalse(check_health(response, TODAY, "pre", VALID_BOOTSTRAP).ok, response.status)

    def test_main_rechaza_la_excepcion_en_post(self):
        code = main(["post", "--app-url", APP_URL, "--migrations-dir", str(MIGRATIONS_DIR),
                     "--app-settings", "no-se-lee.json", "--first-deploy-bootstrap"])
        self.assertEqual(code, 1)


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
        for response in (Response(200), Response(500), Response(404), health(503, "UNHEALTHY", False)):
            self.assertFalse(is_waking_up(response))


class MainTests(unittest.TestCase):
    """main() de punta a punta, con la red reemplazada."""

    def setUp(self):
        self.original_fetch = schema_preflight.fetch
        self.settings = Path(tempfile.mkdtemp()) / "settings.json"
        self.settings.write_text(json.dumps([{"name": "ConnectionStrings__DefaultConnection",
                                              "value": EXPECTED_CONNECTION_REFERENCE}]), encoding="utf-8")

    def tearDown(self):
        schema_preflight.fetch = self.original_fetch
        self.settings.unlink(missing_ok=True)

    def run_main(self, phase, responses, *extra):
        calls = []

        def fake_fetch(url, method="GET", payload=None, **_):
            calls.append(url)
            for prefix, response in responses.items():
                if prefix in url:
                    return response
            raise AssertionError(url)

        schema_preflight.fetch = fake_fetch
        code = main([phase, "--app-url", APP_URL, "--migrations-dir", str(MIGRATIONS_DIR),
                     "--app-settings", str(self.settings), *extra])
        return code, calls

    def test_manda_la_huella_de_v2_con_las_migraciones_del_codigo(self):
        code, calls = self.run_main("post", {"/api/health": health(), "/api/packages": Response(200),
                                             "/api/auth/login": Response(401)})
        self.assertEqual(code, 0)
        expected = fingerprint("turisclick_db_v2", expected_migrations(MIGRATIONS_DIR))
        self.assertIn(f"/api/health?expect={expected}", calls[0])

    def test_post_falla_si_las_sondas_dan_500(self):
        code, _ = self.run_main("post", {"/api/health": health(), "/api/packages": Response(500),
                                         "/api/auth/login": Response(401)})
        self.assertEqual(code, 1)

    def test_post_falla_si_la_huella_no_coincide(self):
        code, _ = self.run_main("post", {"/api/health": health(schema_matches=False), "/api/packages": Response(200),
                                         "/api/auth/login": Response(401)})
        self.assertEqual(code, 1)


class WorkflowTests(unittest.TestCase):
    """backend-v2.yml usa el script como corresponde. Sin PyYAML: se lee el texto por pasos."""

    @classmethod
    def setUpClass(cls):
        text = WORKFLOW.read_text(encoding="utf-8")
        cls.text = text
        deploy_job = text[text.index("\n  deploy:"):]
        cls.steps = re.split(r"\n      - ", deploy_job)[1:]

    def step(self, needle):
        matches = [s for s in self.steps if needle in s]
        self.assertEqual(len(matches), 1, needle)
        return matches[0]

    def index(self, needle):
        return next(i for i, s in enumerate(self.steps) if needle in s)

    def test_nada_se_ignora_si_falla(self):
        self.assertNotIn("continue-on-error", self.text)

    def test_pre_corre_antes_del_deploy_y_post_despues(self):
        self.assertLess(self.index("schema_preflight.py pre"), self.index("az webapp deploy"))
        self.assertGreater(self.index("schema_preflight.py post"), self.index("az webapp deploy"))
        self.assertGreater(self.index("schema_preflight.py post"), self.index("name: Smoke tests"))

    def test_post_es_obligatorio_aunque_fallen_los_pasos_anteriores(self):
        post = self.step("schema_preflight.py post")
        self.assertIn("always()", post)
        self.assertIn("steps.deploy.outcome", post)
        self.assertIn("id: deploy", self.step("az webapp deploy"))

    def test_post_propaga_el_codigo_de_salida(self):
        post = self.step("schema_preflight.py post")
        self.assertIn("|| status=$?", post)
        self.assertIn("exit $status", post)

    def test_la_excepcion_de_primer_deploy_solo_se_pasa_en_pre_y_si_se_pidio(self):
        self.assertNotIn("first-deploy-bootstrap", self.step("schema_preflight.py post"))
        pre = self.step("schema_preflight.py pre")
        self.assertIn('if [ "$FIRST_DEPLOY_BOOTSTRAP" = "true" ]', pre)
        self.assertIn("--active-deployment-id", pre)

    def test_el_input_de_primer_deploy_es_falso_por_defecto(self):
        block = self.text[self.text.index("first_deploy_bootstrap:"):]
        block = block[:block.index("migrations_applied:")]
        self.assertIn("type: boolean", block)
        self.assertIn("default: false", block)

    def test_el_deploy_sigue_siendo_manual_y_solo_desde_master(self):
        self.assertIn("github.event_name == 'workflow_dispatch' && inputs.deploy && github.ref == 'refs/heads/master'",
                      self.text)


if __name__ == "__main__":
    unittest.main()
