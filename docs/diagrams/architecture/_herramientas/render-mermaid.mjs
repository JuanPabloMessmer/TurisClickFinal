// Uso: node render-mermaid.mjs <archivo.mmd>...  -> genera .svg y .png junto a cada fuente.
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import puppeteer from 'puppeteer-core';
const require = createRequire(import.meta.url);
const mermaidJs = path.join(path.dirname(require.resolve('mermaid/package.json')), 'dist', 'mermaid.min.js');
const exe = process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const browser = await puppeteer.launch({ executablePath: exe, args: ['--no-sandbox'], headless: true });
let failed = 0;
try {
  for (const file of process.argv.slice(2)) {
    const page = await browser.newPage();
    await page.setViewport({ width: 1600, height: 1000, deviceScaleFactor: 2 });
    await page.setContent('<!doctype html><html><head><meta charset="utf-8"></head><body style="margin:0;background:#fff"><div id="c"></div></body></html>');
    await page.addScriptTag({ path: mermaidJs });
    const src = fs.readFileSync(file, 'utf8');
    const res = await page.evaluate(async (def) => {
      try {
        mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: 'default',
          fontFamily: 'Arial, Helvetica, sans-serif', flowchart: { htmlLabels: true }, er: { layoutDirection: 'TB' } });
        await mermaid.parse(def);
        const { svg } = await mermaid.render('d', def);
        document.getElementById('c').innerHTML = svg;
        const el = document.querySelector('#c svg');
        // Mermaid emite width="100%" + max-width: se fija el tamaño natural del viewBox para que el
        // diagrama no se encoja al ancho de la ventana (ni en el PNG ni al abrir el SVG suelto).
        const vb = el.viewBox.baseVal;
        el.setAttribute('width', String(Math.ceil(vb.width)));
        el.setAttribute('height', String(Math.ceil(vb.height)));
        el.style.maxWidth = 'none';
        const r = el.getBoundingClientRect();
        return { svg: el.outerHTML, w: Math.ceil(r.width), h: Math.ceil(r.height) };
      } catch (e) { return { error: String(e && e.message || e) }; }
    }, src);
    const base = file.replace(/\.mmd$/, '');
    if (res.error) { console.error(`ERROR ${file}: ${res.error}`); failed++; await page.close(); continue; }
    fs.writeFileSync(base + '.svg', '<?xml version="1.0" encoding="UTF-8"?>\n' + res.svg);
    const el = await page.$('#c svg');
    await el.screenshot({ path: base + '.png', omitBackground: false });
    console.log(`OK ${file} (${res.w}x${res.h})`);
    await page.close();
  }
} finally { await browser.close(); }
process.exit(failed ? 1 : 0);
