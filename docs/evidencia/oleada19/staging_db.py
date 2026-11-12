# -*- coding: utf-8 -*-
"""Crea la base de staging local. Aborta si la conexión no es local.

No imprime credenciales. Sólo host, puerto y nombre de base.
"""
import io, json, os, re, subprocess, sys

SECRETS = os.path.expandvars(
    r'%APPDATA%\Microsoft\UserSecrets\93667b67-3422-45c8-a99e-9cdf5a9100e1\secrets.json')
PSQL = r'C:\Program Files\PostgreSQL\16\bin\psql.exe'
STAGING_DB = 'turisclick_staging_o19'

PROHIBIDAS = {'turisclick_db', 'turisclick_db_v2', 'turisclick_v2_dev', 'turisclick_v2_test'}
HOSTS_LOCALES = {'localhost', '127.0.0.1', '::1'}


def partes(cs):
    g = lambda k: (re.search(k + r'=([^;]+)', cs, re.I) or [None, None])[1]
    return {'host': g('Host'), 'port': g('Port') or '5432', 'db': g('Database'),
            'user': g('Username') or g('User ?Id'), 'pwd': g('Password')}


def main():
    if not os.path.exists(SECRETS):
        print('ERROR: no hay user-secrets en la ruta esperada.'); return 1

    secrets = json.load(io.open(SECRETS, encoding='utf-8-sig'))
    cs = secrets.get('ConnectionStrings:DefaultConnection')
    if not cs:
        print('ERROR: no hay ConnectionStrings:DefaultConnection.'); return 1

    p = partes(cs)

    # ─── COMPROBACIÓN DE SEGURIDAD ────────────────────────────────────────────────────
    print('Destino de la conexión local:')
    print('  Host     : %s' % p['host'])
    print('  Port     : %s' % p['port'])
    print('  Database : %s  (la de desarrollo, NO se toca)' % p['db'])
    print()

    if (p['host'] or '').lower() not in HOSTS_LOCALES:
        print('ABORTADO: el host "%s" no es local. No se escribe nada.' % p['host']); return 2
    if STAGING_DB in PROHIBIDAS:
        print('ABORTADO: el nombre de staging colisiona con una base existente.'); return 2

    print('Comprobacion OK: host local. Se puede crear la base de staging.')
    print()

    env = dict(os.environ); env['PGPASSWORD'] = p['pwd'] or ''
    base = [PSQL, '-h', p['host'], '-p', p['port'], '-U', p['user'], '-d', 'postgres',
            '-v', 'ON_ERROR_STOP=1', '-tA']

    existe = subprocess.run(base + ['-c', "SELECT 1 FROM pg_database WHERE datname='%s'" % STAGING_DB],
                            env=env, capture_output=True, text=True)
    if existe.returncode != 0:
        print('ERROR consultando pg_database:', existe.stderr.strip()[:200]); return 1

    if existe.stdout.strip() == '1':
        print('La base "%s" YA EXISTIA. No se recrea.' % STAGING_DB)
    else:
        crear = subprocess.run(base + ['-c', 'CREATE DATABASE %s' % STAGING_DB],
                               env=env, capture_output=True, text=True)
        if crear.returncode != 0:
            print('ERROR creando la base:', crear.stderr.strip()[:300]); return 1
        print('Base "%s" CREADA.' % STAGING_DB)

    listado = subprocess.run(base + ['-c',
        "SELECT datname FROM pg_database WHERE datname LIKE 'turisclick%' ORDER BY 1"],
        env=env, capture_output=True, text=True)
    print()
    print('Bases locales de TurisClick:')
    for d in listado.stdout.split():
        print('  -', d, '  <-- staging de esta corrida' if d == STAGING_DB else '')
    return 0


if __name__ == '__main__':
    sys.exit(main())
