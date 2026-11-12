# -*- coding: utf-8 -*-
"""S-2: un token emitido por staging no puede autenticar en Azure V2.

Es la prueba que demuestra que la clave de firma está aislada. Si Azure devolviera 200,
el aislamiento estaría roto y habría que detener todo.

No imprime credenciales ni el token.
"""
import io, json, os, re, sys, urllib.request, urllib.error

STAGING = 'http://localhost:5299'
AZURE = 'https://app-turisclick-v2-api.azurewebsites.net'
SECRETS = os.path.expandvars(
    r'%APPDATA%\Microsoft\UserSecrets\93667b67-3422-45c8-a99e-9cdf5a9100e1\secrets.json')


def pedir(url, metodo='GET', cuerpo=None, token=None, timeout=60):
    datos = json.dumps(cuerpo).encode() if cuerpo is not None else None
    req = urllib.request.Request(url, data=datos, method=metodo)
    req.add_header('Accept', 'application/json')
    if datos is not None:
        req.add_header('Content-Type', 'application/json')
    if token:
        req.add_header('Authorization', 'Bearer ' + token)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, json.loads(r.read().decode() or '{}')
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode() or '{}')
        except Exception:
            return e.code, {}
    except Exception as e:
        return 0, {'error': str(e)[:120]}


secrets = json.load(io.open(SECRETS, encoding='utf-8-sig'))
pwd = secrets.get('Seed:AdminPassword')
if not pwd:
    print('ABORTADO: no hay Seed:AdminPassword.'); sys.exit(1)

st, cuerpo = pedir(STAGING + '/api/auth/login', 'POST',
                   {'email': 'admin@turisclick.dev', 'password': pwd})
print('Login ADMIN en staging ............ HTTP %s' % st)
if st != 200:
    print('ABORTADO: no se pudo autenticar en staging.'); sys.exit(1)

token = cuerpo.get('accessToken') or cuerpo.get('token') or cuerpo.get('access_token')
if not token:
    print('ABORTADO: la respuesta no trae token. Claves:', sorted(cuerpo.keys())); sys.exit(1)
print('Token de staging obtenido ......... <no se imprime, %d caracteres>' % len(token))
print()

# El token funciona donde fue emitido
st_ok, _ = pedir(STAGING + '/api/admin/companies?page=1&pageSize=1', token=token)
print('Token de staging -> STAGING ....... HTTP %s   %s' % (st_ok, 'OK' if st_ok == 200 else 'inesperado'))

# Y NO debe funcionar en producción
st_az, _ = pedir(AZURE + '/api/admin/companies?page=1&pageSize=1', token=token)
print('Token de staging -> AZURE V2 ...... HTTP %s   %s' % (
    st_az, 'CORRECTO: rechazado' if st_az in (401, 403) else '*** AISLAMIENTO ROTO ***'))
print()
print('S-2:', 'APROBADA' if st_az in (401, 403) and st_ok == 200 else 'FALLIDA')
