import { useRef, useState } from 'react'
import { uploadDestinationImage } from './api'
import { getUploadErrorMessage, type ImageSelection } from './imageFile'

/**
 * Convierte la imagen elegida en el `imageUrl` que se manda al guardar el destino. Si el archivo es nuevo lo
 * sube primero (con progreso); si el guardado falla después (ej. nombre repetido) y se reintenta con el mismo
 * archivo, reutiliza la URL ya subida en vez de subirlo otra vez.
 */
export function useDestinationImageUpload() {
  const [progress, setProgress] = useState<number | null>(null)
  const lastUpload = useRef<{ file: File; url: string } | null>(null)

  const resolveImageUrl = async (selection: ImageSelection, currentUrl?: string | null): Promise<string | null> => {
    if (selection.kind === 'current') return currentUrl ?? null
    if (selection.kind === 'removed') return null
    if (lastUpload.current?.file === selection.file) return lastUpload.current.url

    setProgress(0)
    try {
      const { url } = await uploadDestinationImage(selection.file, setProgress)
      if (!url) throw new Error('El servidor no devolvió la dirección de la imagen.')
      lastUpload.current = { file: selection.file, url }
      return url
    } catch (error) {
      throw new Error(getUploadErrorMessage(error))
    } finally {
      setProgress(null)
    }
  }

  return { progress, resolveImageUrl }
}
