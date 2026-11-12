# -*- coding: utf-8 -*-
"""Ejecuta un comando con la configuración de STAGING LOCAL inyectada por variables de entorno.

Las variables de entorno tienen prioridad sobre user-secrets en ASP.NET Core, así que esto
**no sobrescribe nada**: la configuración de desarrollo queda intacta.

La clave JWT de staging es propia y distinta de la de desarrollo y de la de producción.
Nada se escribe en disco y nada se imprime.

Uso:  python run_staging.py <comando> [args...]
"""
import hashlib, io, json, os, re, subprocess, sys

SECRETS = os.path.expandvars(
    r'%APPDATA%\Microsoft\UserSecrets\93667b67-3422-45c8-a99e-9cdf5a9100e1\secrets.json')
STAGING_DB = 'turisclick_staging_o19'
HOSTS_LOCALES = {'localhost', '127.0.0.1', '::1'}


def cargar():
    secrets = json.load(io.open(SECRETS, encoding='utf-8-sig'))
    cs = secrets['ConnectionStrings:DefaultConnection']

    host = (re.search(r'Host=([^;]+)', cs, re.I) or [None, ''])[1]
    if host.lower() not in HOSTS_LOCALES:
        sys.stderr.write('ABORTADO: la conexion base no es local (%s).\n' % host)
        sys.exit(2)

    db_actual = (re.search(r'Database=([^;]+)', cs, re.I) or [None, ''])[1]
    if db_actual == STAGING_DB:
        sys.stderr.write('ABORTADO: la conexion base ya apunta a staging.\n')
        sys.exit(2)

    # Sólo se cambia el nombre de la base. Usuario, contraseña y host quedan como estaban.
    cs_staging = re.sub(r'Database=[^;]+', 'Database=' + STAGING_DB, cs, flags=re.I)

    # Clave JWT exclusiva de staging: derivada de un literal propio, nunca la de desarrollo
    # ni la de producción. Determinista para que todas las corridas compartan sesión.
    jwt = hashlib.sha512(b'<semilla de la clave JWT de staging, redactada>').hexdigest()

    return cs_staging, jwt, secrets.get('Seed:AdminPassword', '')


def entorno():
    cs, jwt, admin_pwd = cargar()
    env = dict(os.environ)
    env['ConnectionStrings__DefaultConnection'] = cs
    env['Jwt__Key'] = jwt
    env['Flights__Provider'] = 'Fake'          # nunca Duffel
    env['Ai__Provider'] = 'Deterministic'      # sin dependencia de Ollama
    env['ASPNETCORE_ENVIRONMENT'] = 'Development'
    env['Seed__Enabled'] = 'true'
    env['STAGING_ADMIN_PASSWORD'] = admin_pwd  # lo consume el script de pruebas, no se imprime
    env.pop('ConnectionStrings__TestDatabase', None)
    return env


if __name__ == '__main__':
    if len(sys.argv) < 2:
        # Sin comando: sólo informa a dónde apunta, sin secretos.
        cs, jwt, _ = cargar()
        print('Host     :', (re.search(r'Host=([^;]+)', cs, re.I))[1])
        print('Database :', (re.search(r'Database=([^;]+)', cs, re.I))[1])
        print('Jwt:Key  : <propia de staging, %d caracteres>' % len(jwt))
        print('Flights  : Fake')
        sys.exit(0)
    sys.exit(subprocess.run(sys.argv[1:], env=entorno()).returncode)
