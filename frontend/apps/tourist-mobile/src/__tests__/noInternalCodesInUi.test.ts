/// <reference types="node" />
// El tsconfig de la app declara `types: ["jest"]` a propósito, para que el código de la app no pueda usar
// APIs de Node por accidente. Esta prueba sí las necesita —lee el árbol de archivos—, así que las trae sólo
// para este archivo en vez de abrirlas para todo el proyecto.
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'

/**
 * Chequeo estático: ningún identificador interno puede llegar a la pantalla del viajero.
 *
 * Los códigos de caso de uso (`UC-T-08`, `UC-AI-06`…) son el vocabulario con el que escribimos la
 * especificación, y ahí sirven. Lo que no pueden hacer es aparecer en la app: alguien que está eligiendo un
 * viaje no tiene por qué leer la numeración de un documento de requisitos.
 *
 * Se permiten en comentarios y se prohíben en todo lo demás, porque la regla es fácil de romper sin darse
 * cuenta: basta una cadena copiada de la especificación a un `<Text>`.
 */

const ROOTS = [join(__dirname, '..'), join(__dirname, '..', '..', 'app')]

/** Identificadores internos que nunca van en una interfaz. */
const FORBIDDEN = /\b(UC-[A-Z]{1,4}-\d{2}|UC-SYS-\d{2})\b/

function collectSourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((entry: string) => {
    const path = join(directory, entry)
    if (statSync(path).isDirectory()) {
      if (entry === '__tests__' || entry === 'test-utils' || entry === 'node_modules') return []
      return collectSourceFiles(path)
    }
    return /\.tsx?$/.test(entry) && !/\.test\.tsx?$/.test(entry) ? [path] : []
  })
}

/**
 * Saca los comentarios para que el chequeo mire sólo lo que puede terminar renderizado. No es un parser de
 * TypeScript: alcanza para distinguir una nota del código de un texto que se muestra.
 */
function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1')
}

describe('la app no muestra identificadores internos', () => {
  const files = ROOTS.flatMap(collectSourceFiles)

  it('encuentra archivos para revisar', () => {
    // Si el recorrido se rompe, el test pasaría vacío y no protegería nada.
    expect(files.length).toBeGreaterThan(30)
  })

  it.each(files.map((file) => [file.split(/[\/]/).slice(-2).join('/'), file]))(
    'sin códigos de caso de uso en %s',
    (_relative, file) => {
      const code = stripComments(readFileSync(file, 'utf8'))
      const match = code.match(FORBIDDEN)

      expect(match ? `"${match[0]}" quedó en código que puede renderizarse` : null).toBeNull()
    },
  )
})
