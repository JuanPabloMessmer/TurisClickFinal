"""
Mide si un diagrama es legible impreso en A4.

Para cada SVG: lee su tamaño y el cuerpo de letra más chico que usa, lo escala para que entre en el área útil
de una hoja A4 (márgenes de 25 mm: 160 × 247 mm vertical, 247 × 160 mm apaisada, se elige la que deja el texto
más grande) y calcula cuántos puntos tipográficos mide ese texto mínimo en el papel.

Criterio: ≥ 7 pt legible · 6–7 pt justo · < 6 pt ilegible (cuerpo mínimo habitual en figuras de tesis: 7–8 pt).

Uso:  python legibilidad_a4.py <archivo.svg | carpeta>...
"""
import re
import sys
from pathlib import Path

PT_PER_MM = 1 / 0.3528
AREAS = {"vertical": (160, 247), "apaisada": (247, 160)}


def svg_info(path: Path):
    s = path.read_text(encoding="utf-8", errors="ignore")
    head = s[:3000]
    vb = re.search(r'viewBox="\s*[-\d.]+\s+[-\d.]+\s+([\d.]+)\s+([\d.]+)"', head)
    w = re.search(r'\bwidth="([\d.]+)(?:px)?"', head)
    h = re.search(r'\bheight="([\d.]+)(?:px)?"', head)
    width = float(w.group(1)) if w else float(vb.group(1))
    height = float(h.group(1)) if h else float(vb.group(2))
    sizes = [float(x) for x in re.findall(r'font-size[=:]\s*"?([\d.]+)(?:px)?', s)]
    sizes = [x for x in sizes if x >= 6]  # descarta tamaños 0/ocultos que no se dibujan como texto legible
    return width, height, min(sizes) if sizes else 14.0


def evaluate(path: Path):
    w, h, fmin = svg_info(path)
    best = max(((name, min(aw / w, ah / h)) for name, (aw, ah) in AREAS.items()), key=lambda t: t[1])
    pt = fmin * best[1] * PT_PER_MM
    verdict = "legible" if pt >= 7 else ("justo" if pt >= 6 else "ILEGIBLE")
    return w, h, fmin, best[0], pt, verdict


def main(args):
    files = []
    for a in args:
        p = Path(a)
        files += sorted(p.rglob("*.svg")) if p.is_dir() else [p]
    bad = 0
    print(f"{'diagrama':58} {'px':>11} {'fuente mín':>10} {'hoja':>9} {'texto en A4':>11}")
    for f in files:
        w, h, fmin, orient, pt, verdict = evaluate(f)
        bad += verdict == "ILEGIBLE"
        print(f"{f.name:58} {int(w):5}x{int(h):<5} {fmin:8.1f}px {orient:>9} {pt:7.1f} pt  {verdict}")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
