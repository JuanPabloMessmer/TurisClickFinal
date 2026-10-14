"""
Genera el esquema DBML y las vistas ERD (PlantUML) de TurisClick V2 a partir del snapshot de EF Core.

Fuente de verdad: src/TurisClick.Api/Infrastructure/Database/Migrations/TurisClickDbContextModelSnapshot.cs
(el modelo que EF Core compara contra cada migración nueva). No se lee ninguna base de datos.

Uso, desde la raíz del repositorio:
    python docs/diagrams/architecture/_herramientas/generar_erd.py

Salida (se sobrescribe):
    docs/diagrams/architecture/tecnico/04-base-de-datos/turisclick-v2.dbml
    docs/diagrams/architecture/tecnico/04-base-de-datos/04a-erd-general.puml ... 04g-erd-ia-itinerarios.puml

Lo único que NO sale del snapshot, y por eso está escrito a mano abajo:
  * ENUMS: qué enum de C# respalda cada columna varchar (el snapshot sólo ve `string`).
  * LOGICAL_REFS: columnas que apuntan a otra tabla SIN restricción FK en la base.
  * Las vistas: qué tablas entran en cada diagrama.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
SNAPSHOT = ROOT / "src/TurisClick.Api/Infrastructure/Database/Migrations/TurisClickDbContextModelSnapshot.cs"
OUT = ROOT / "docs/diagrams/architecture/tecnico/04-base-de-datos"

# --------------------------------------------------------------------------------------------------
# 1. Parser del snapshot
# --------------------------------------------------------------------------------------------------

def _blocks(src: str):
    """Devuelve (nombre_entidad, cuerpo) por cada modelBuilder.Entity("...", b => { ... })."""
    for m in re.finditer(r'modelBuilder\.Entity\("([\w\.]+)", b =>\s*\{', src):
        start, depth, i = m.end(), 1, m.end()
        while depth:
            depth += {"{": 1, "}": -1}.get(src[i], 0)
            i += 1
        yield m.group(1).split(".")[-1], src[start:i - 1]


def parse(path: Path):
    src = path.read_text(encoding="utf-8-sig")
    ents: dict[str, dict] = {}
    for name, body in _blocks(src):
        e = ents.setdefault(name, {"table": None, "cols": [], "pk": [], "idx": [], "fk": [], "checks": []})
        t = re.search(r'b\.ToTable\("(\w+)"', body)
        if t:
            e["table"] = t.group(1)
        for pm in re.finditer(r'b\.Property<([^>]+)>\("(\w+)"\)(.*?);', body, re.S):
            clr, prop, rest = pm.group(1).strip(), pm.group(2), pm.group(3)
            col = re.search(r'HasColumnName\("(\w+)"\)', rest)
            dbt = re.search(r'HasColumnType\("([^"]+)"\)', rest)
            dsql = re.search(r'HasDefaultValueSql\("([^"]*)"\)', rest)
            dval = re.search(r'HasDefaultValue\(([^)]*)\)', rest)
            nullable = clr.endswith("?") or (clr in ("string", "byte[]") and "IsRequired()" not in rest)
            e["cols"].append({
                "prop": prop, "name": col.group(1) if col else prop, "type": dbt.group(1) if dbt else clr,
                "nullable": nullable,
                "default": f"`{dsql.group(1)}`" if dsql else (dval.group(1).strip('"') if dval else None),
                "default_is_str": bool(dval and dval.group(1).startswith('"')),
            })
        k = re.search(r'b\.HasKey\(([^)]*)\)', body)
        if k:
            e["pk"] = re.findall(r'"(\w+)"', k.group(1))
        for im in re.finditer(r'b\.HasIndex\(([^)]*)\)(.*?);', body, re.S):
            args, rest = im.group(1), im.group(2)
            cols = re.findall(r'"(\w+)"', args)
            dbname = re.search(r'HasDatabaseName\("(\w+)"\)', rest)
            # HasIndex(new[] {...}, "nombre") — el último literal es el nombre del índice, no una columna.
            named = re.search(r'\},\s*"(\w+)"\s*$', args.strip())
            if named:
                cols = cols[:-1]
            filt = re.search(r'HasFilter\("([^"]*)"\)', rest)
            e["idx"].append({"cols": cols, "unique": "IsUnique()" in rest,
                             "filter": filt.group(1) if filt else None,
                             "name": (dbname.group(1) if dbname else (named.group(1) if named else None))})
        for cm in re.finditer(r'HasCheckConstraint\("(\w+)", "([^"]*)"\)', body):
            e["checks"].append((cm.group(1), cm.group(2)))
        for fm in re.finditer(r'b\.HasOne\("([\w\.]+)",\s*(?:"\w+"|null)\)(.*?);', body, re.S):
            tgt, rest = fm.group(1).split(".")[-1], fm.group(2)
            fk = re.search(r'HasForeignKey\(([^)]*)\)', rest)
            if not fk:
                continue
            ob = re.search(r'OnDelete\(DeleteBehavior\.(\w+)\)', rest)
            e["fk"].append({"target": tgt, "props": re.findall(r'"(\w+)"', fk.group(1)),
                            "ondelete": ob.group(1) if ob else "NoAction",
                            "one_to_one": "WithOne(" in rest, "required": "IsRequired()" in rest})
    # entidades con tabla = las que tienen columnas (los bloques de "sólo relaciones" se fusionaron arriba)
    model = {}
    for name, e in ents.items():
        table = e["table"] or name
        model[table] = e
        e["entity"] = name
    ent2table = {e["entity"]: t for t, e in model.items()}
    for t, e in model.items():
        p2c = {c["prop"]: c["name"] for c in e["cols"]}
        e["pk_cols"] = [p2c.get(p, p) for p in e["pk"]]
        for i in e["idx"]:
            i["cols"] = [p2c.get(p, p) for p in i["cols"]]
        for f in e["fk"]:
            f["cols"] = [p2c.get(p, p) for p in f["props"]]
            f["table"] = ent2table[f["target"]]
    return model


# --------------------------------------------------------------------------------------------------
# 2. Lo que el snapshot no sabe
# --------------------------------------------------------------------------------------------------

ENUMS = {  # tabla.columna -> (enum C#, valores). Se guardan como varchar, NO como ENUM nativo de PostgreSQL.
    "users.role": ("UserRole", "TOURIST, PROVIDER, ADMIN"),
    "users.status": ("UserStatus", "ACTIVE, SUSPENDED"),
    "companies.status": ("CompanyStatus", "PENDING_APPROVAL, APPROVED, REJECTED, SUSPENDED"),
    "destinations.type": ("DestinationType", "COUNTRY, REGION, CITY"),
    "experiences.status": ("PublicationStatus", "DRAFT, PUBLISHED, UNPUBLISHED, SUSPENDED"),
    "packages.status": ("PublicationStatus", "DRAFT, PUBLISHED, UNPUBLISHED, SUSPENDED"),
    "experience_availabilities.status": ("AvailabilitySlotStatus", "OPEN, CLOSED"),
    "package_availabilities.status": ("AvailabilitySlotStatus", "OPEN, CLOSED"),
    "package_items.kind": ("PackageItemKind", "EXPERIENCE_REFERENCE, DESCRIPTIVE"),
    "reservations.status": ("ReservationStatus", "PENDING_PAYMENT, CONFIRMED, PAYMENT_FAILED, CANCELLING, CANCELLED, EXPIRED"),
    "reservation_items.status": ("ReservationItemStatus", "PENDING_PAYMENT, CONFIRMED, CANCELLED, EXPIRED"),
    "reservation_items.product_type": ("ProductType", "EXPERIENCE, PACKAGE"),
    "ai_itinerary_items.product_type": ("ProductType", "EXPERIENCE, PACKAGE"),
    "ai_conversations.status": ("AiConversationStatus", "ACTIVE, CLOSED"),
    "ai_conversations.travel_pace": ("TravelPace", "RELAXED, BALANCED, INTENSE"),
    "ai_messages.sender": ("MessageSender", "TOURIST, AI"),
    "ai_itineraries.status": ("AiItineraryStatus", "DRAFT, SAVED, BOOKED, DISCARDED"),
    "tourist_preferences.travel_pace": ("TravelPace", "RELAXED, BALANCED, INTENSE"),
    "tourist_preferences.travel_party": ("TravelParty", "SOLO, COUPLE, FRIENDS, FAMILY"),
    "tourist_preferences.budget_level": ("BudgetLevel", "ECONOMY, MODERATE, PREMIUM"),
    "package_flight_rules.cabin_class": ("FlightCabinClass", "ECONOMY, PREMIUM_ECONOMY, BUSINESS, FIRST"),
    "flight_quotes.status": ("FlightQuoteStatus", "QUOTED, CONFIRMED, PRICE_CHANGED, EXPIRED, UNAVAILABLE"),
    "flight_bookings.status": ("FlightBookingStatus", "PENDING, ORDERING, CONFIRMED, FAILED, RECONCILIATION_REQUIRED, CANCELLED"),
    "payment_transactions.type": ("PaymentTransactionType", "CHARGE, VOID, REFUND"),
    "payment_transactions.status": ("PaymentTransactionStatus", "PENDING, SUCCEEDED, FAILED"),
    "payment_transactions.component": ("PaymentComponent", "PACKAGE, EXPERIENCE, FLIGHT"),
    "reservation_cancellations.status": ("ReservationCancellationStatus", "QUOTED, ACCEPTED, COMPLETED, REFUND_PENDING, REQUIRES_REVIEW, FAILED, EXPIRED"),
    "reservation_cancellation_lines.component": ("PaymentComponent", "PACKAGE, EXPERIENCE, FLIGHT"),
}

# Columnas que guardan el id de otra tabla SIN FK en la base (verificado en las IEntityTypeConfiguration).
LOGICAL_REFS = [
    ("payment_transactions", "reservation_item_id", "reservation_items"),
    ("reservation_cancellation_lines", "reservation_item_id", "reservation_items"),
    ("flight_quotes", "tourist_id", "users"),
]

MODULES = {
    "Auth": ["users", "refresh_tokens"],
    "Companies": ["companies"],
    "Destinations": ["destinations"],
    "Categories": ["categories"],
    "Experiences": ["experiences", "experience_images", "experience_availabilities", "experience_categories"],
    "Packages": ["packages", "package_items", "package_images", "package_availabilities", "package_categories"],
    "Reservations": ["reservations", "reservation_items", "payment_transactions",
                     "reservation_cancellations", "reservation_cancellation_lines"],
    "Flights": ["package_flight_rules", "flight_quotes", "flight_bookings"],
    "Ai": ["ai_conversations", "ai_conversation_categories", "ai_messages", "ai_itineraries", "ai_itinerary_items"],
    "Preferences": ["tourist_preferences", "tourist_preference_categories"],
}
MODULE_COLOR = {
    "Auth": "#E3F2FD", "Companies": "#E8EAF6", "Destinations": "#E0F2F1", "Categories": "#E0F2F1",
    "Experiences": "#F1F8E9", "Packages": "#FFFDE7", "Reservations": "#FFF3E0", "Flights": "#FCE4EC",
    "Ai": "#F3E5F5", "Preferences": "#EDE7F6",
}
TABLE_MODULE = {t: m for m, ts in MODULES.items() for t in ts}

VIEWS = [
    # archivo, título, tablas principales (todas las columnas), tablas de referencia (sólo PK)
    ("04b-erd-usuarios-proveedores", "Usuarios, roles, proveedores y preferencias",
     ["users", "refresh_tokens", "companies", "tourist_preferences", "tourist_preference_categories"],
     ["categories"]),
    ("04c-erd-destinos-categorias", "Destinos, categorías y clasificación del catálogo",
     ["destinations", "categories", "experience_categories", "package_categories"],
     ["experiences", "packages"]),
    ("04d-erd-experiencias-paquetes", "Experiencias, paquetes y disponibilidad",
     ["experiences", "experience_images", "experience_availabilities",
      "packages", "package_items", "package_images", "package_availabilities"],
     ["companies", "destinations"]),
    ("04e-erd-reservas-pagos", "Reservas, libro de pagos y cancelación con reembolso",
     ["reservations", "reservation_items", "payment_transactions",
      "reservation_cancellations", "reservation_cancellation_lines"],
     ["users", "companies", "experiences", "experience_availabilities", "packages",
      "package_availabilities", "ai_itineraries"]),
    ("04f-erd-vuelos", "Vuelos en paquetes: regla, cotización y reserva aérea",
     ["package_flight_rules", "flight_quotes", "flight_bookings"],
     ["packages", "package_availabilities", "reservations", "users"]),
    ("04g-erd-ia-itinerarios", "Persistencia del asistente de IA: conversaciones e itinerarios",
     ["ai_conversations", "ai_conversation_categories", "ai_messages", "ai_itineraries", "ai_itinerary_items"],
     ["users", "destinations", "categories", "experiences", "experience_availabilities",
      "packages", "package_availabilities", "reservations"]),
]

# --------------------------------------------------------------------------------------------------
# 3. Utilidades
# --------------------------------------------------------------------------------------------------

def short_type(t: str) -> str:
    t = t.replace("character varying", "varchar").replace("timestamp with time zone", "timestamptz")
    return {"time without time zone": "time", "timestamp without time zone": "timestamp"}.get(t, t)


def unique_single_cols(e) -> set[str]:
    return {i["cols"][0] for i in e["idx"] if i["unique"] and len(i["cols"]) == 1 and not i["filter"]}


def partial_unique_single_cols(e) -> set[str]:
    return {i["cols"][0] for i in e["idx"] if i["unique"] and len(i["cols"]) == 1 and i["filter"]}


def fk_cols(e) -> set[str]:
    return {c for f in e["fk"] for c in f["cols"]}


def ordered_cols(e):
    pk, fks = e["pk_cols"], fk_cols(e)
    first = [c for c in e["cols"] if c["name"] in pk]
    first.sort(key=lambda c: pk.index(c["name"]))
    mid = [c for c in e["cols"] if c["name"] not in pk and c["name"] in fks]
    rest = [c for c in e["cols"] if c["name"] not in pk and c["name"] not in fks]
    return first + mid + rest


def relationships(model):
    """(padre, hijo, columnas, requerido, uno_a_uno, on_delete) por cada FK real."""
    rels = []
    for t, e in model.items():
        uniq = unique_single_cols(e)
        for f in e["fk"]:
            cols = f["cols"]
            nullable = any(c["nullable"] for c in e["cols"] if c["name"] in cols)
            one = f["one_to_one"] or (len(cols) == 1 and (cols[0] in uniq or cols == e["pk_cols"]))
            rels.append({"parent": f["table"], "child": t, "cols": cols, "required": not nullable,
                         "one": one, "ondelete": f["ondelete"]})
    return rels


# --------------------------------------------------------------------------------------------------
# 4. DBML
# --------------------------------------------------------------------------------------------------

def dbml(model) -> str:
    out = ["// TurisClick V2 — esquema físico PostgreSQL",
           "// GENERADO por docs/diagrams/architecture/_herramientas/generar_erd.py desde",
           "// src/TurisClick.Api/Infrastructure/Database/Migrations/TurisClickDbContextModelSnapshot.cs",
           "// (14 migraciones, última 0014_Oleada13_AltaDeOperadoresPorAdmin). No editar a mano.",
           "",
           "Project turisclick_v2 {",
           "  database_type: 'PostgreSQL'",
           "  Note: '''Los estados y tipos de dominio se guardan como varchar con el nombre del enum de C#",
           "(HasConversion<string>), no como tipos ENUM nativos de PostgreSQL. Las columnas uuid usan",
           "gen_random_uuid() por defecto.'''",
           "}", ""]
    for module, tables in MODULES.items():
        for t in tables:
            e = model[t]
            out.append(f"Table {t} {{")
            uniq, puniq = unique_single_cols(e), partial_unique_single_cols(e)
            for c in ordered_cols(e):
                attrs = []
                if c["name"] in e["pk_cols"] and len(e["pk_cols"]) == 1:
                    attrs.append("pk")
                if not c["nullable"]:
                    attrs.append("not null")
                if c["name"] in uniq and c["name"] not in e["pk_cols"]:
                    attrs.append("unique")
                if c["default"] is not None:
                    d = c["default"]
                    attrs.append(f"default: '{d}'" if c["default_is_str"] else f"default: {d}")
                key = f"{t}.{c['name']}"
                notes = []
                if key in ENUMS:
                    notes.append(f"enum C# {ENUMS[key][0]}: {ENUMS[key][1]}")
                for (lt, lc, target) in LOGICAL_REFS:
                    if lt == t and lc == c["name"]:
                        notes.append(f"referencia lógica a {target}.id SIN restricción FK")
                if c["name"] in puniq:
                    notes.append("índice único parcial, ver indexes")
                if notes:
                    attrs.append("note: '" + "; ".join(notes).replace("'", "\\'") + "'")
                a = f" [{', '.join(attrs)}]" if attrs else ""
                out.append(f"  {c['name']} {short_type(c['type'])}{a}")
            idx_lines = []
            if len(e["pk_cols"]) > 1:
                idx_lines.append(f"    ({', '.join(e['pk_cols'])}) [pk]")
            for i in e["idx"]:
                if i["unique"] and len(i["cols"]) == 1 and not i["filter"]:
                    continue  # ya marcado como unique en la columna
                cols = i["cols"][0] if len(i["cols"]) == 1 else "(" + ", ".join(i["cols"]) + ")"
                ia = []
                if i["unique"]:
                    ia.append("unique")
                if i["name"]:
                    ia.append(f"name: '{i['name']}'")
                if i["filter"]:
                    ia.append("note: 'parcial: WHERE " + i["filter"].replace("'", "\\'") + "'")
                idx_lines.append(f"    {cols}" + (f" [{', '.join(ia)}]" if ia else ""))
            if idx_lines:
                out += ["", "  indexes {"] + idx_lines + ["  }"]
            if e["checks"]:
                out += ["", "  Note: '''CHECK:"] + [f"- {n}: {expr}".replace("'''", "") for n, expr in e["checks"]] + ["'''"]
            out += ["}", ""]
    for r in relationships(model):
        sym = "-" if r["one"] else ">"
        od = {"Cascade": "cascade", "Restrict": "restrict", "SetNull": "set null", "NoAction": "no action"}[r["ondelete"]]
        out.append(f"Ref: {r['child']}.{r['cols'][0]} {sym} {r['parent']}.id [delete: {od}]"
                   if r["parent"] != "tourist_preferences" else
                   f"Ref: {r['child']}.{r['cols'][0]} > tourist_preferences.user_id [delete: {od}]")
    out.append("")
    for module, tables in MODULES.items():
        out.append(f"TableGroup {module} {{")
        out += [f"  {t}" for t in tables]
        out += ["}", ""]
    return "\n".join(out)


# --------------------------------------------------------------------------------------------------
# 5. PlantUML
# --------------------------------------------------------------------------------------------------

HEADER = """@startuml {name}
' GENERADO por docs/diagrams/architecture/_herramientas/generar_erd.py a partir del snapshot de EF Core.
' Para cambiar el contenido, editar la vista en el generador y volver a ejecutarlo.
!pragma layout smetana
!include ../_tema.iuml
hide circle
hide methods
skinparam linetype ortho
skinparam nodesep 40
skinparam ranksep 55
title {title}
"""

LEGEND_KEYS = """legend bottom left
<b>Notación</b> (Ingeniería de la Información / pata de gallo)
  ● (punto lleno) columna NOT NULL · <<PK>> clave primaria · <<FK>> clave foránea · <<UK>> única
  ""||"" exactamente uno · ""|o"" cero o uno · ""o{"" cero o muchos
  línea continua = FK real en PostgreSQL · línea punteada = referencia lógica sin FK
  entidades grises = tablas de otras vistas (sólo su PK)
{enums}endlegend
"""


def entity(model, t, full=True) -> list[str]:
    e = model[t]
    color = MODULE_COLOR[TABLE_MODULE[t]] if full else "#EEEEEE"
    stereo = f" <<{TABLE_MODULE[t]}>>" if full else " <<ref>>"
    lines = [f'entity "{t}" as {t}{stereo} {color} {{']
    uniq, fks, puniq = unique_single_cols(e), fk_cols(e), partial_unique_single_cols(e)
    cols = ordered_cols(e) if full else [c for c in e["cols"] if c["name"] in e["pk_cols"]]
    logical = {lc for (lt, lc, _) in LOGICAL_REFS if lt == t}
    wrote_sep = False
    for c in cols:
        n = c["name"]
        tags = []
        if n in e["pk_cols"]:
            tags.append("PK")
        if n in fks:
            tags.append("FK")
        if n in uniq and n not in e["pk_cols"]:
            tags.append("UK")
        if n in puniq:
            tags.append("UK parcial")
        if n in logical:
            tags.append("ref. lógica")
        if full and not wrote_sep and n not in e["pk_cols"]:
            lines.append("  --")
            wrote_sep = True
        star = "* " if not c["nullable"] else "  "
        tag = (" <<" + ", ".join(tags) + ">>") if tags else ""
        lines.append(f"  {star}{n} : {short_type(c['type'])}{tag}")
    lines.append("}")
    return lines


def rel_line(r) -> str:
    left = "||" if r["required"] else "|o"
    right = "o|" if r["one"] else "o{"
    od = {"Cascade": "CASCADE", "Restrict": "RESTRICT", "SetNull": "SET NULL", "NoAction": "NO ACTION"}[r["ondelete"]]
    return f'{r["parent"]} {left}--{right} {r["child"]} : "{", ".join(r["cols"])}\\n[{od}]"'


def view(model, rels, name, title, main, refs) -> str:
    shown = set(main) | set(refs)
    out = [HEADER.format(name=name, title=title)]
    by_module: dict[str, list[str]] = {}
    for t in main:
        by_module.setdefault(TABLE_MODULE[t], []).append(t)
    for module, tables in by_module.items():
        out.append(f'package "Módulo {module}" as pkg_{module} {{')
        for t in tables:
            out += ["  " + l for l in entity(model, t)]
        out.append("}")
    for t in refs:
        out += entity(model, t, full=False)
    out.append("")
    for r in rels:
        if r["parent"] in shown and r["child"] in shown and (r["child"] in main or r["parent"] in main):
            out.append(rel_line(r))
    for (lt, lc, target) in LOGICAL_REFS:
        if lt in shown and target in shown:
            out.append(f'{target} |o..o{{ {lt} : "{lc}\\n(sin FK)"')
    enums = ""
    for t in main:
        for c in ordered_cols(model[t]):
            k = f"{t}.{c['name']}"
            if k in ENUMS:
                enums += f'  ""{k}"" → {ENUMS[k][1]}\n'
    if enums:
        enums = "<b>Dominios de las columnas varchar</b> (enums de C#, guardados como texto)\n" + enums
    out.append(LEGEND_KEYS.replace("{enums}", enums))
    checks = [(t, n, x) for t in main for (n, x) in model[t]["checks"]]
    if checks:
        out.append("note as checks")
        out.append("<b>Restricciones CHECK en la base</b> (expresión completa en turisclick-v2.dbml)")
        for t, n, x in checks:
            out.append('""' + n + '""')
        out.append("end note")
    out.append("@enduml\n")
    return "\n".join(out)


def general(model, rels) -> str:
    # Sin title: en una vista de 29 tablas las líneas largas lo atraviesan. Va como caption, abajo.
    out = [HEADER.format(name="04a-erd-general", title="").replace("title \n", "")]
    out.append("caption TurisClick V2 — modelo de datos completo: 29 tablas agrupadas por módulo (sólo claves)")
    out.append("skinparam nodesep 25\nskinparam ranksep 45")
    for module, tables in MODULES.items():
        out.append(f'package "{module}" as pkg_{module} {{')
        for t in tables:
            e = model[t]
            fks = fk_cols(e)
            logical = {lc for (lt, lc, _) in LOGICAL_REFS if lt == t}
            out.append(f'  entity "{t}" as {t} {MODULE_COLOR[module]} {{')
            for c in ordered_cols(e):
                if c["name"] in e["pk_cols"] or c["name"] in fks or c["name"] in logical:
                    tags = [x for x, ok in (("PK", c["name"] in e["pk_cols"]), ("FK", c["name"] in fks),
                                            ("ref. lógica", c["name"] in logical)) if ok]
                    out.append(f"    {'* ' if not c['nullable'] else '  '}{c['name']} <<{', '.join(tags)}>>")
            out.append("  }")
        out.append("}")
    for r in rels:
        left = "||" if r["required"] else "|o"
        right = "o|" if r["one"] else "o{"
        out.append(f"{r['parent']} {left}--{right} {r['child']}")
    for (lt, lc, target) in LOGICAL_REFS:
        out.append(f"{target} |o..o{{ {lt}")
    # Sólo layout: users y companies se referencian mutuamente; sin esta pista Smetana los separa y la
    # curva entre ambos sale del lienzo.
    out.append("companies -[hidden]down- users")
    out.append(LEGEND_KEYS.replace("{enums}", "").replace("  entidades grises = tablas de otras vistas (sólo su PK)\n", ""))
    out.append("@enduml\n")
    return "\n".join(out)


def check_enums_against_code() -> list[str]:
    """Compara cada enum de ENUMS con su declaración en src/ (mismos valores, mismo orden)."""
    sources = [f.read_text(encoding="utf-8-sig") for f in (ROOT / "src/TurisClick.Api").rglob("*.cs")]
    errors = []
    for key, (name, values) in ENUMS.items():
        body = next((m.group(1) for s in sources
                     for m in [re.search(r"public enum " + name + r"\b[^{]*\{(.*?)\}", s, re.S)] if m), None)
        if body is None:
            errors.append(f"{key}: no se encontró 'enum {name}' en src/")
            continue
        body = re.sub(r"//.*", "", body)
        code = re.findall(r"\b([A-Z][A-Z_]+)\b", body)
        if code != [v.strip() for v in values.split(",")]:
            errors.append(f"{key}: el código declara {code}")
    return errors


def main() -> int:
    model = parse(SNAPSHOT)
    expected = {t for ts in MODULES.values() for t in ts}
    if set(model) != expected:
        print("ERROR: las tablas del snapshot no coinciden con MODULES:",
              "faltan", sorted(set(model) - expected), "sobran", sorted(expected - set(model)), file=sys.stderr)
        return 1
    for k in ENUMS:
        t, c = k.split(".")
        if not any(col["name"] == c for col in model[t]["cols"]):
            print(f"ERROR: ENUMS menciona {k}, que no existe", file=sys.stderr)
            return 1
    enum_errors = check_enums_against_code()
    if enum_errors:
        print("ERROR: ENUMS desactualizado:", *enum_errors, sep="\n  ", file=sys.stderr)
        return 1
    for lt, lc, target in LOGICAL_REFS:
        if lc in fk_cols(model[lt]):
            print(f"ERROR: {lt}.{lc} ya tiene FK: sacarlo de LOGICAL_REFS", file=sys.stderr)
            return 1
    rels = relationships(model)
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "turisclick-v2.dbml").write_text(dbml(model), encoding="utf-8")
    (OUT / "04a-erd-general.puml").write_text(general(model, rels), encoding="utf-8")
    for name, title, main_t, refs in VIEWS:
        (OUT / f"{name}.puml").write_text(view(model, rels, name, title, main_t, refs), encoding="utf-8")
    print(f"{len(model)} tablas, {sum(len(e['cols']) for e in model.values())} columnas, "
          f"{len(rels)} FKs, {len(LOGICAL_REFS)} referencias lógicas -> {OUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
