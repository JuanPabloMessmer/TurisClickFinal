# -*- coding: utf-8 -*-
"""Batería funcional de la Oleada 19 contra STAGING LOCAL.

Sólo toca http://localhost:5299. Ninguna escritura fuera de esa API.
No imprime credenciales ni tokens.
"""
import io, json, os, re, sys, time, urllib.request, urllib.error
from datetime import date, timedelta
try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

sys_out=None
API = 'http://localhost:5299'
SECRETS = os.path.expandvars(
    r'%APPDATA%\Microsoft\UserSecrets\93667b67-3422-45c8-a99e-9cdf5a9100e1\secrets.json')
EVID = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'evidencia-o19.json')

evidencia = []
resultados = []


def pedir(path, metodo='GET', cuerpo=None, token=None, timeout=90):
    url = API + path
    datos = json.dumps(cuerpo).encode('utf-8') if cuerpo is not None else None
    req = urllib.request.Request(url, data=datos, method=metodo)
    req.add_header('Accept', 'application/json')
    if datos is not None:
        req.add_header('Content-Type', 'application/json')
    if token:
        req.add_header('Authorization', 'Bearer ' + token)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            txt = r.read().decode('utf-8')
            return r.status, (json.loads(txt) if txt.strip() else {})
    except urllib.error.HTTPError as e:
        txt = e.read().decode('utf-8', 'replace')
        try:
            return e.code, (json.loads(txt) if txt.strip() else {})
        except Exception:
            return e.code, {'raw': txt[:300]}
    except Exception as e:
        return 0, {'error': str(e)[:200]}


def reg(path, metodo, status, cuerpo):
    evidencia.append({'ts': time.strftime('%Y-%m-%d %H:%M:%S'), 'metodo': metodo,
                      'path': path, 'status': status, 'respuesta': cuerpo})


def llamar(path, metodo='GET', cuerpo=None, token=None):
    st, body = pedir(path, metodo, cuerpo, token)
    reg(path, metodo, st, body)
    return st, body


def check(id_, etiqueta, ok, detalle=''):
    resultados.append({'id': id_, 'prueba': etiqueta, 'resultado': 'APROBADA' if ok else 'FALLIDA',
                       'detalle': detalle})
    print('%-7s %-5s %s%s' % (id_, 'OK' if ok else 'FAIL', etiqueta,
                              ('  — ' + detalle) if detalle else ''))
    return ok


secrets = json.load(io.open(SECRETS, encoding='utf-8-sig'))
admin_pwd = secrets['Seed:AdminPassword']
suf = str(int(time.time()))[-6:]

print('=' * 78)
print('BATERIA FUNCIONAL OLEADA 19 — STAGING LOCAL  %s' % API)
print('=' * 78)
print()

# ── Preparación ──────────────────────────────────────────────────────────────────────
st, b = llamar('/api/auth/login', 'POST', {'email': 'admin@turisclick.dev', 'password': admin_pwd})
admin = b.get('accessToken')
if not admin:
    print('ABORTADO: sin sesión de admin (HTTP %s)' % st); sys.exit(1)
print('Preparación: admin autenticado')

st, dests = llamar('/api/destinations?type=CITY', token=admin)
items = dests if isinstance(dests, list) else (dests.get('items') or [])
ciudad = next((d for d in items if 'paz' in (d.get('name') or '').lower()), None) or (items[0] if items else None)
if not ciudad:
    print('ABORTADO: no hay ciudades en staging.'); sys.exit(1)
print('Preparación: destino "%s"' % ciudad.get('name'))

prov_email = 'qa.operador.%s@turisclick.dev' % suf
st, b = llamar('/api/admin/provider-accounts', 'POST', {
    'companyName': 'TurisClick QA — PRUEBA TECNICA %s' % suf,
    'legalDocument': 'QA-%s' % suf, 'contactEmail': prov_email,
    'firstName': 'Operador', 'lastName': 'QA', 'email': prov_email, 'approve': True}, admin)
if st not in (200, 201):
    print('ABORTADO: no se creó el operador (HTTP %s)' % st); sys.exit(1)
temp = b.get('temporaryPassword'); company_id = b.get('companyId') or (b.get('company') or {}).get('id')
print('Preparación: operador creado')

st, b = llamar('/api/auth/login', 'POST', {'email': prov_email, 'password': temp})
st, b = llamar('/api/auth/change-password', 'POST',
               {'currentPassword': temp, 'newPassword': '<contrasena de prueba, redactada>'}, b.get('accessToken'))
prov = b.get('accessToken')
print('Preparación: contraseña temporal cambiada')

salida = (date.today() + timedelta(days=45)).isoformat()
st, pkg = llamar('/api/packages', 'POST', {
    'title': '[PRUEBA TECNICA · NO RESERVAR] Validacion Oleada 19 %s' % suf,
    'description': 'Paquete de prueba para validar la Oleada 19. No es oferta real.',
    'destinationId': ciudad['id'], 'categoryIds': [], 'durationDays': 3,
    'price': 480, 'currency': 'USD',
    'items': [{'dayNumber': 1, 'sortOrder': 1, 'kind': 'DESCRIPTIVE', 'title': 'Llegada'}],
    'images': [],
    'cancellationPolicy': [{'minDaysBefore': 30, 'refundPercentage': 100},
                           {'minDaysBefore': 15, 'refundPercentage': 50},
                           {'minDaysBefore': 0, 'refundPercentage': 0}]}, prov)
pkg_id = pkg.get('id')
st, av = llamar('/api/packages/%s/availability' % pkg_id, 'POST',
                {'departureDate': salida, 'totalSlots': 1}, prov)
av_id = av.get('id')
llamar('/api/packages/%s/publish' % pkg_id, 'POST', None, prov)
st_rule, rule = llamar('/api/packages/%s/flight-rule' % pkg_id, 'PUT', {
    'destinationIata': 'LPB', 'allowedOriginIatas': ['VVI'], 'cabinClass': 'ECONOMY',
    'outboundOffsetDays': 0, 'inboundOffsetDays': 0, 'roundTrip': True}, prov)
print('Preparación: paquete publicado con regla VVI -> LPB (HTTP %s)' % st_rule)

tur_email = 'qa.turista.%s@turisclick.dev' % suf
st, b = llamar('/api/auth/register', 'POST', {
    'firstName': 'Turista', 'lastName': 'QA', 'email': tur_email, 'password': '<contrasena de prueba, redactada>'})
tur = b.get('accessToken')
print('Preparación: turista registrado')
print()
print('-' * 78)

# ── Pruebas ──────────────────────────────────────────────────────────────────────────
st, q = llamar('/api/packages/%s/flight-quotes' % pkg_id, 'POST',
               {'originIata': 'VVI', 'packageAvailabilityId': av_id, 'travelers': 1}, tur)
ops = q.get('options') or []
check('T-01', 'Cotizar el vuelo del paquete desde VVI (UC-T-23)', st == 200 and len(ops) > 0,
      'HTTP %s · %d ofertas' % (st, len(ops)))

st_bad, qbad = llamar('/api/packages/%s/flight-quotes' % pkg_id, 'POST',
                      {'originIata': 'CBB', 'packageAvailabilityId': av_id, 'travelers': 1}, tur)
check('T-02', 'Un origen NO permitido se rechaza (UC-P-15)', st_bad >= 400,
      'HTTP %s' % st_bad)

if not ops:
    print('\nSin ofertas: no se puede continuar con reserva y pago.')
    json.dump({'resultados': resultados, 'evidencia': evidencia}, io.open(EVID, 'w', encoding='utf-8'),
              ensure_ascii=False, indent=1)
    sys.exit(1)

o = ops[0]
quote_id = o.get('quoteId')
precio = o.get('flightPrice') or {}

st, rev = llamar('/api/flight-quotes/%s/revalidate' % quote_id, 'POST', None, tur)
check('T-03', 'Revalidar la cotización (UC-T-20)', st == 200,
      'HTTP %s · outcome=%s' % (st, rev.get('outcome')))

st, res = llamar('/api/reservations', 'POST',
                 {'packageAvailabilityId': av_id, 'travelers': 1, 'flightQuoteId': quote_id}, tur)
res_id = res.get('id'); vuelo = (res.get('flight') or {}).get('status')
check('T-04', 'Reservar paquete + vuelo en una operación (UC-T-21)',
      st in (200, 201) and res_id is not None, 'HTTP %s · vuelo=%s' % (st, vuelo))

st, dup = llamar('/api/reservations', 'POST',
                 {'packageAvailabilityId': av_id, 'travelers': 1, 'flightQuoteId': quote_id}, tur)
check('T-05', 'La misma cotización no crea una segunda reserva',
      dup.get('id') == res_id, 'devuelve %s' % ('la misma' if dup.get('id') == res_id else 'otra'))

pasajero = {'givenName': 'Prueba', 'familyName': 'Sintetica', 'bornOn': '1990-01-15',
            'gender': 'f', 'title': 'ms', 'email': 'qa.vuelos@turisclick.dev',
            'phoneNumber': '+59170000000'}
st, pay = llamar('/api/reservations/%s/pay' % res_id, 'POST',
                 {'success': True, 'acceptPriceChanges': True, 'travelers': [pasajero],
                  'acceptedFlightPrice': precio}, tur)
if pay.get('requiresFlightPriceAcceptance'):
    st, pay = llamar('/api/reservations/%s/pay' % res_id, 'POST',
                     {'success': True, 'acceptPriceChanges': True, 'travelers': [pasajero],
                      'acceptedFlightPrice': pay.get('flightCurrentPrice')}, tur)
aprobado = pay.get('paymentApproved')
check('T-06', 'REGRESION BUG-10: el paquete con vuelo SI se cobra', st == 200 and aprobado is True,
      'HTTP %s · paymentApproved=%s' % (st, aprobado))

st, det = llamar('/api/reservations/%s' % res_id, token=tur)
check('T-07', 'La reserva queda CONFIRMED (UC-SYS-07)', det.get('status') == 'CONFIRMED',
      'status=%s · vuelo=%s' % (det.get('status'), (det.get('flight') or {}).get('status')))

st, bus = llamar('/api/flights/search', 'POST',
                 {'originIata': 'VVI', 'destinationIata': 'LPB', 'departureDate': salida,
                  'passengers': 1, 'cabinClass': 'ECONOMY'})
paquetes = bus.get('packages') or []
check('T-08', 'La búsqueda devuelve el paquete que cubre VVI -> LPB',
      any(p.get('id') == pkg_id for p in paquetes), '%d paquete(s) en la ruta' % len(paquetes))

st, cq = llamar('/api/reservations/%s/cancellation-quote' % res_id, 'POST', None, tur)
lineas = cq.get('lines') or []
comps = {l.get('component') for l in lineas}
check('T-09', 'Presupuesto de cancelación por componente (UC-T-22)',
      st == 200 and 'PACKAGE' in comps and 'FLIGHT' in comps, 'componentes=%s' % sorted(comps))

st, can = llamar('/api/reservations/%s/cancel' % res_id, 'POST',
                 {'cancellationQuoteId': cq.get('quoteId')}, tur)
check('T-10', 'Cancelación ejecutada (UC-T-11)', st == 200,
      'HTTP %s · estado=%s' % (st, (can.get('cancellation') or {}).get('status')))

st, led = llamar('/api/admin/reservations/%s/payments' % res_id, token=admin)
check('T-11', 'Libro de pagos del administrador (UC-A-10)', st == 200, 'HTTP %s' % st)

st_p, _ = llamar('/api/companies/me/flights', token=prov)
st_a, _ = llamar('/api/admin/flights', token=admin)
check('T-12', 'B-04: vuelos de operador responden 501 FEATURE_NOT_AVAILABLE',
      st_p == 501 and st_a == 501, 'operador=%s · admin=%s' % (st_p, st_a))

# Suspensión: la puerta de atrás con un availabilityId ya conocido
st, av2 = llamar('/api/packages/%s/availability' % pkg_id, 'POST',
                 {'departureDate': (date.today() + timedelta(days=60)).isoformat(), 'totalSlots': 1}, prov)
av2_id = av2.get('id')
st_sus, _ = llamar('/api/admin/companies/%s/suspend' % company_id, 'POST', None, admin)
st_back, _ = llamar('/api/reservations', 'POST',
                    {'packageAvailabilityId': av2_id, 'travelers': 1}, tur)
check('T-13', 'Empresa suspendida: reservar con un id conocido da 404 (UC-A-08)',
      st_sus == 200 and st_back == 404, 'suspend=%s · reserva=%s' % (st_sus, st_back))

print('-' * 78)
ap = sum(1 for r in resultados if r['resultado'] == 'APROBADA')
print('APROBADAS %d / %d' % (ap, len(resultados)))
for r in resultados:
    if r['resultado'] == 'FALLIDA':
        print('   FALLIDA: %s %s — %s' % (r['id'], r['prueba'], r['detalle']))

json.dump({'generado': time.strftime('%Y-%m-%d %H:%M:%S'), 'api': API,
           'paquete': pkg_id, 'reserva': res_id, 'empresa': company_id,
           'resultados': resultados, 'evidencia': evidencia},
          io.open(EVID, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('\nEvidencia: %s' % EVID)
