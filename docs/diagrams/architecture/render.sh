#!/usr/bin/env bash
# Regenera todos los diagramas de docs/diagrams/architecture (SVG + PNG) y valida el DBML.
#
# Requisitos:
#   - Java 17+ y PLANTUML_JAR apuntando a plantuml-1.2025.4.jar (Maven Central:
#     net.sourceforge.plantuml:plantuml:1.2025.4). No hace falta Graphviz: los .puml usan Smetana.
#   - Node 20+ y `npm install` dentro de _herramientas/ (mermaid, puppeteer-core, @dbml/core).
#   - Un navegador Chromium instalado; BROWSER_PATH si no es Edge en la ruta por defecto de Windows.
#   - Python 3 sólo para regenerar los ERD desde el snapshot de EF Core (paso 1).
#
# Uso, desde la raíz del repositorio:   bash docs/diagrams/architecture/render.sh
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
: "${PLANTUML_JAR:?Definí PLANTUML_JAR con la ruta a plantuml-1.2025.4.jar}"

echo "1/4 ERD y DBML desde el snapshot de EF Core"
python "$HERE/_herramientas/generar_erd.py"

echo "2/4 Validación del DBML"
node "$HERE/_herramientas/validar-dbml.mjs" "$HERE/tecnico/04-base-de-datos/turisclick-v2.dbml"

echo "3/4 PlantUML (sintaxis + SVG + PNG)"
mapfile -t PUML < <(find "$HERE" -name '*.puml' -not -path '*/_herramientas/*' | sort)
java -jar "$PLANTUML_JAR" -checkonly "${PUML[@]}"
java -jar "$PLANTUML_JAR" -tsvg "${PUML[@]}"
java -jar "$PLANTUML_JAR" -tpng "${PUML[@]}"

echo "4/4 Mermaid (sintaxis + SVG + PNG)"
mapfile -t MMD < <(find "$HERE" -name '*.mmd' -not -path '*/_herramientas/*' | sort)
node "$HERE/_herramientas/render-mermaid.mjs" "${MMD[@]}"
echo "Listo: ${#PUML[@]} PlantUML y ${#MMD[@]} Mermaid."
