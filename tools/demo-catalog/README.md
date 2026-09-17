# Catálogo demo de TurisClick V2

Catálogo de demostración para la defensa de tesis: **atractivos reales de Bolivia** operados por
**proveedores ficticios**, cargado en el backend V2 de Azure usando sólo los endpoints normales de la API
(registro de proveedor, aprobación del admin, experiencias, disponibilidad, paquetes). Sin SQL, sin
migraciones, sin `DevelopmentSeeder` y sin tocar V1.

| Archivo | Qué hace |
|---|---|
| `catalog.mjs` | Los datos: proveedores y su zona, ciudades nuevas, experiencias, paquetes, calendario. |
| `resolve-images.mjs` | Busca imágenes con licencia libre en Wikimedia Commons y escribe `images.manifest.json` y `ATTRIBUTIONS.md`. |
| `images.manifest.json` | URL, autor, licencia y página de origen de cada imagen (la atribución que el modelo no guarda). |
| `ATTRIBUTIONS.md` | Créditos legibles, generados desde el manifiesto. |
| `load-catalog.mjs` | Carga idempotente contra la API. `--check` valida el catálogo sin llamar a la API. |
| `validate-catalog.mjs` | Validación automatizada, sólo lecturas, contra la API real. |
| `run-catalog.ps1` | Ejecuta el loader o los validadores con las contraseñas (Key Vault + DPAPI) sin exponerlas. |
| `commons.mjs` | Acceso compartido a Wikimedia Commons (filtro de licencias libres). |
| `destinations.mjs` / `resolve-destination-images.mjs` | Imagen representativa por destino → `destination-images.manifest.json` y `ATTRIBUTIONS-DESTINOS.md`. |
| `validate-product-wave.mjs` | Validación real de calendario, imágenes de destinos, onboarding, reserva y asistente IA. |

## Uso

Requisitos: Node 20+, Azure CLI con sesión iniciada (para leer `seed-admin-password` de Key Vault) y Windows
(las contraseñas demo se guardan con DPAPI en `%USERPROFILE%\.turisclick-secrets`).

```powershell
node tools/demo-catalog/load-catalog.mjs --check            # catálogo coherente (zonas, ownership, imágenes)
node tools/demo-catalog/resolve-images.mjs                  # sólo si se agregan experiencias
.\tools\demo-catalog\run-catalog.ps1 load-catalog.mjs       # carga / sincroniza (idempotente)
.\tools\demo-catalog\run-catalog.ps1 validate-catalog.mjs   # validación
```

Volver a correr el loader no duplica nada: busca destinos por nombre y experiencias/paquetes por título
dentro de cada proveedor, actualiza el contenido con `PUT`, agrega sólo las fechas que falten y publica lo que
no esté publicado. `TURISCLICK_API` apunta los scripts a otro backend (por defecto, Azure V2).

## Qué contiene

- **8 proveedores ficticios**, cada uno opera sólo en su zona (el loader lo verifica antes de cargar):

  | Empresa (ficticia) | Zona |
  |---|---|
  | Altura Viva Expediciones | La Paz, El Alto, Oruro, Uyuni |
  | Lago y Yunga Senderos | Copacabana, Coroico |
  | Quebrada Roja Andina | Potosí, Tupiza |
  | Llajta Verde Aventura | Cochabamba, Quillacollo, Sipe Sipe, Tiquipaya, Villa Tunari, Torotoro |
  | Charcas Patrimonio Vivo | Sucre |
  | Cepa Chapaca Rutas | Tarija, Bermejo, Villamontes |
  | Curichi Oriente Travesías | Santa Cruz de la Sierra, Cotoca, Santiago del Torno, Samaipata, San Ignacio de Velasco |
  | Jichi Amazonía Expediciones | Rurrenabaque, Trinidad, Riberalta |

  Los nombres se buscaron en la web antes de usarlos para no coincidir con empresas reales. Los NIT son
  `NIT-DEMO-*`, los correos son `@turisclick.dev` y no se cargan teléfonos.
- **90 experiencias** en 26 ciudades y **13 paquetes**, cuyas referencias apuntan sólo a experiencias de la
  misma empresa.
- **5 ciudades nuevas** (Copacabana, Coroico, Rurrenabaque, Samaipata, Torotoro), creadas con
  `POST /api/admin/destinations` bajo su departamento real, porque el pedido las nombra y no había una ciudad
  del seed donde ubicarlas sin falsear la geografía. Chiquitos se cubre con San Ignacio de Velasco, que ya
  existía. Incallajta (en Pocona) y Tiwanaku se ofrecen como excursiones de día desde Cochabamba y La Paz.
- **Precios**: en BOB, **de referencia para la demo** (no son tarifas reales), escalonados por tipo: de
  60–130 las visitas urbanas cortas a 650–780 las jornadas técnicas o en 4x4.
- **Calendario**: una salida semanal por experiencia hasta el 31/12/2026 y quincenal hasta el 31/03/2027,
  respetando los días en que el atractivo funciona (feria de El Alto jueves y domingos, Tarabuco domingo,
  museos cerrados los lunes). Los paquetes salen cada 21 días y el de Sucre sale en viernes para llegar a
  Tarabuco en domingo.

## Imágenes y atribución

Todas las imágenes son de **Wikimedia Commons** con licencia **CC BY, CC BY-SA, CC0 o dominio público**
(`resolve-images.mjs` descarta NC/ND, archivos con restricciones, marcas de agua, mapas, logos y escaneos) y se
enlazan desde `upload.wikimedia.org` en su miniatura oficial de 1280 px, sin modificar.

`ExperienceImage` y `PackageImage` sólo guardan `url` + `isCover`, así que la atribución **no se agregó al
modelo** (no hubo migración): queda en `images.manifest.json` (máquina) y `ATTRIBUTIONS.md` (personas), con
autor, licencia y enlace a la página de cada archivo. Si más adelante se quiere mostrar el crédito en la app,
la opción natural es agregar `author`/`license`/`sourceUrl` a esas entidades; eso sí requiere migración y
queda a decisión del equipo.

## Imágenes de destinos

41 de las 47 ciudades tienen imagen (todas las que tienen experiencias). Las 6 restantes (Caranavi, Viacha,
Colcapirhua, Ivirgarzama, San Lucas y Cabezas) no tienen en Commons una foto representativa con licencia
libre: quedan sin imagen y la app muestra un fondo neutro. El loader aplica las imágenes con el `PUT` de
admin (que reemplaza nombre + imagen, conservando el nombre).

## Datos demo previos

La API no tiene `DELETE` para experiencias, paquetes, usuarios ni reservas, así que nada se borró:

- `proveedor.demo@turisclick.dev` se **reutilizó** como *Altura Viva Expediciones* (antes "Andes y Salar
  Turismo"). Sus experiencias de La Paz y Uyuni y el paquete "Bolivia esencial" se actualizaron al contenido
  del catálogo.
- "Sucre colonial: centro histórico" (de esa cuenta, fuera de su zona) quedó **despublicada**. Conserva una
  reserva CONFIRMED de QA que la API no permite cancelar sin política de reembolso.
- `qa.turista@turisclick.dev` quedó **suspendida** (sigue existiendo, con sus reservas de prueba).
- Las fechas creadas antes para las experiencias reutilizadas se conservan: en esos días la hora puede ser
  la anterior (la API no permite borrar disponibilidad).
- `turista.demo@turisclick.dev` queda ACTIVE y **sin reservas**, lista para la demo.
- `qa.asistente@turisclick.dev` es la cuenta QA de `validate-product-wave.mjs`: guarda preferencias, conversa con
  el asistente y reserva/cancela (sus reservas quedan CANCELLED). La experiencia "La Paz desde el Teleférico"
  suma los fines de semana de abril 2027, generados por el calendario durante esa validación.

## Fuentes de la investigación

Instituciones:
[Ministerio de Culturas y Turismo](https://www.turismoyculturas.gob.bo/) ·
[Viceministerio de Turismo](https://www.turismo.produccion.gob.bo/) ·
[Conoce Bolivia](https://conocebolivia.turismoyculturas.gob.bo/) ·
[SERNAP](https://www.sernap.gob.bo/) ·
[UNESCO: Bolivia](https://whc.unesco.org/en/statesparties/bo) ·
[GAM Tarija](https://www.tarija.bo/tarija/conozcamos-tarija/) ·
[Turismo Tarija: Tariquía](https://www.turismo.tarija.gob.bo/es/tarija/areas-protegidas/35-la-reserva-nacional-de-flora-y-fauna-tariquia) ·
[GAM Villa Tunari](https://www.villatunari.gob.bo/turismo/) ·
[GAM San Ignacio de Velasco](https://www.gamsiv.gob.bo/cultura-y-turismo/) ·
[Viceministerio de Turismo: Oruro](https://www.turismo.produccion.gob.bo/?page_id=9031)

Prensa y guías:
[Unitel: maravillas cruceñas](https://noticias.unitel.bo/sociedad/10-maravillas-crucenas-que-debes-conocer-si-visitas-santa-cruz-HGUN157055) ·
[La Región: Torotoro](https://www.laregion.bo/ocho-asombrosos-lugares-en-toro-toro-que-no-te-puedes-perder/) ·
[La Región: Villa Montes](https://www.laregion.bo/ocho-rutas-turisticas-para-conocer-villa-montes-en-tarija/) ·
[La Región: San Ignacio de Velasco](https://www.laregion.bo/san-ignacio-de-velasco/) ·
[Opinión: Torotoro](https://www.opinion.com.bo/articulo/revista-asi/toro-toro-destino-magico-cavernas-cascadas-huellas-dinosaurios/20240922000004956151.html) ·
[Wikipedia: Copacabana (Bolivia)](https://en.wikipedia.org/wiki/Copacabana,_Bolivia) ·
[Wikipedia: Villa Tunari](https://en.wikipedia.org/wiki/Villa_Tunari)
