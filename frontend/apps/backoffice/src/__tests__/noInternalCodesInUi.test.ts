import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

/**
 * Chequeo estático: ningún identificador interno puede llegar a la pantalla.
 *
 * Los códigos de caso de uso (`UC-A-01`, `UC-P-07`…) son nuestro vocabulario para escribir especificaciones, y
 * sirven para eso. Lo que no pueden hacer es aparecer en el panel de un operador: alguien que administra su
 * empresa no tiene por qué leer la numeración interna de un documento de requisitos.
 *
 * Se permiten en comentarios —ahí son útiles, conectan el código con la documentación— y se prohíben en todo lo
 * demás. Este test existe porque la regla es fácil de romper sin darse cuenta: basta un `description=` copiado
 * de la especificación.
 */

const SOURCE_ROOT = join(import.meta.dirname, '..')

/** Identificadores internos que nunca van en una interfaz. */
const FORBIDDEN = /\b(UC-[A-Z]{1,4}-\d{2}|UC-SYS-\d{2})\b/

function collectSourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry)
    if (statSync(path).isDirectory()) {
      if (entry === '__tests__' || entry === 'test-setup') return []
      return collectSourceFiles(path)
    }
    return /\.tsx?$/.test(entry) && !/\.test\.tsx?$/.test(entry) ? [path] : []
  })
}

/**
 * Saca los comentarios para que el chequeo mire sólo lo que puede terminar renderizado. No es un parser de
 * TypeScript y no pretende serlo: alcanza para distinguir una nota del código de un texto que se muestra.
 */
function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1')
}

describe('la interfaz no muestra identificadores internos', () => {
  const files = collectSourceFiles(SOURCE_ROOT)

  it('encuentra archivos para revisar', () => {
    // Si el recorrido se rompe, el test pasaría vacío y no protegería nada.
    expect(files.length).toBeGreaterThan(30)
  })

  it.each(files.map((file) => [file.slice(SOURCE_ROOT.length + 1), file]))(
    'sin códigos de caso de uso en %s',
    (_relative, file) => {
      const code = stripComments(readFileSync(file, 'utf8'))
      const match = code.match(FORBIDDEN)

      expect(
        match,
        match ? `"${match[0]}" quedó en código que puede renderizarse. Reemplazalo por copy en español.` : '',
      ).toBeNull()
    },
  )
})
