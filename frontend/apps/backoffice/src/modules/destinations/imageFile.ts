import { isAxiosError } from 'axios'
import { getErrorMessage } from '@/lib/errors'

/** Mismo tope y mismos formatos que DestinationImageService en el backend, que es quien decide de verdad. */
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024
export const ACCEPTED_IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'] as const
const ACCEPTED_EXTENSIONS = ['.jpg', '.jpeg', '.png', '.webp']

export const IMAGE_MESSAGES = {
  unsupported: 'Formato no admitido. Subí una imagen JPG, PNG o WebP.',
  tooLarge: 'La imagen pesa más de 5 MB. Elegí una imagen más liviana.',
  empty: 'El archivo está vacío. Elegí otra imagen.',
  network: 'No se pudo subir la imagen. Revisá tu conexión e intentá de nuevo.',
} as const

/**
 * Validación previa para avisar antes de subir nada. Algunos navegadores no informan el tipo (`file.type`
 * vacío): en ese caso alcanza con la extensión, y el backend igual revisa la firma real del archivo.
 */
export function validateImageFile(file: File): string | null {
  const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase()
  const typeOk = file.type
    ? (ACCEPTED_IMAGE_TYPES as readonly string[]).includes(file.type)
    : ACCEPTED_EXTENSIONS.includes(extension)
  if (!typeOk) return IMAGE_MESSAGES.unsupported
  if (file.size === 0) return IMAGE_MESSAGES.empty
  if (file.size > MAX_IMAGE_BYTES) return IMAGE_MESSAGES.tooLarge
  return null
}

/** El 413 lo corta un proxy o Kestrel antes de llegar al backend y viene sin un detalle útil. */
export function getUploadErrorMessage(error: unknown): string {
  if (isAxiosError(error)) {
    if (error.response?.status === 413) return IMAGE_MESSAGES.tooLarge
    if (!error.response) return IMAGE_MESSAGES.network
  }
  return getErrorMessage(error)
}

export function formatFileSize(bytes: number): string {
  return bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`
}

/**
 * Qué hacer con la imagen al guardar: dejar la actual (o ninguna), subir un archivo nuevo o quitarla.
 * Elegir un archivo no sube nada: la subida ocurre recién al guardar, así cancelar no deja archivos sueltos.
 */
export type ImageSelection = { kind: 'current' } | { kind: 'new'; file: File } | { kind: 'removed' }
