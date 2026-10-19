import { AxiosError, AxiosHeaders } from 'axios'
import { describe, expect, it } from 'vitest'
import { formatFileSize, getUploadErrorMessage, IMAGE_MESSAGES, MAX_IMAGE_BYTES, validateImageFile } from '../imageFile'

function file(name: string, type: string, size = 1024) {
  return new File([new Uint8Array(size)], name, { type })
}

describe('validateImageFile', () => {
  it.each([
    ['foto.jpg', 'image/jpeg'],
    ['foto.png', 'image/png'],
    ['foto.webp', 'image/webp'],
  ])('acepta %s', (name, type) => {
    expect(validateImageFile(file(name, type))).toBeNull()
  })

  it.each([
    ['animada.gif', 'image/gif'],
    ['logo.svg', 'image/svg+xml'],
    ['doc.pdf', 'application/pdf'],
    ['foto.heic', 'image/heic'],
  ])('rechaza %s con un mensaje en español', (name, type) => {
    expect(validateImageFile(file(name, type))).toBe(IMAGE_MESSAGES.unsupported)
  })

  it('sin tipo informado por el navegador decide por la extensión', () => {
    expect(validateImageFile(file('foto.JPEG', ''))).toBeNull()
    expect(validateImageFile(file('foto.bmp', ''))).toBe(IMAGE_MESSAGES.unsupported)
  })

  it('rechaza más de 5 MB y acepta exactamente 5 MB', () => {
    expect(validateImageFile(file('grande.jpg', 'image/jpeg', MAX_IMAGE_BYTES + 1))).toBe(IMAGE_MESSAGES.tooLarge)
    expect(validateImageFile(file('justa.jpg', 'image/jpeg', MAX_IMAGE_BYTES))).toBeNull()
  })

  it('rechaza un archivo vacío', () => {
    expect(validateImageFile(file('vacia.png', 'image/png', 0))).toBe(IMAGE_MESSAGES.empty)
  })
})

describe('getUploadErrorMessage', () => {
  const axiosError = (status?: number, data?: unknown) =>
    new AxiosError('fallo', 'ERR', undefined, undefined, status
      ? { status, statusText: '', headers: {}, config: { headers: new AxiosHeaders() }, data }
      : undefined)

  it('un 413 del servidor o de un proxy se explica como tamaño', () => {
    expect(getUploadErrorMessage(axiosError(413))).toBe(IMAGE_MESSAGES.tooLarge)
  })

  it('sin respuesta es un problema de conexión', () => {
    expect(getUploadErrorMessage(axiosError())).toBe(IMAGE_MESSAGES.network)
  })

  it('cualquier otro error muestra el detalle del backend', () => {
    expect(getUploadErrorMessage(axiosError(503, { detail: 'No se pudo guardar la imagen.' }))).toBe('No se pudo guardar la imagen.')
  })
})

describe('formatFileSize', () => {
  it('muestra KB o MB', () => {
    expect(formatFileSize(300 * 1024)).toBe('300 KB')
    expect(formatFileSize(2.5 * 1024 * 1024)).toBe('2.5 MB')
  })
})
