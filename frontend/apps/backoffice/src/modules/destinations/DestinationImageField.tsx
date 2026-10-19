import { ImagePlus, RotateCcw, Trash2 } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { ACCEPTED_IMAGE_TYPES, formatFileSize, type ImageSelection, validateImageFile } from './imageFile'

interface DestinationImageFieldProps {
  /** Imagen guardada del destino (edición). Puede ser una URL externa previa (ej. Wikimedia) o una subida. */
  currentUrl?: string | null
  value: ImageSelection
  onChange: (value: ImageSelection) => void
  /** null mientras no se está subiendo; 0–100 durante la subida. */
  progress: number | null
  disabled?: boolean
}

/** UC-A-04 — elegir la imagen representativa del destino desde la computadora, con vista previa. */
export function DestinationImageField({ currentUrl, value, onChange, progress, disabled }: DestinationImageFieldProps) {
  const inputId = useId()
  const hintId = useId()
  const errorId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const [validationError, setValidationError] = useState<string | null>(null)
  const preview = usePreviewUrl()
  const previewUrl = preview.urlFor(value.kind === 'new' ? value.file : null)

  const shownUrl = value.kind === 'new' ? previewUrl : value.kind === 'current' ? currentUrl : null
  const hasImage = !!shownUrl
  const uploading = progress !== null

  const onFileSelected = (file: File | undefined) => {
    if (!file) return
    const error = validateImageFile(file)
    setValidationError(error)
    if (error) return
    preview.createFor(file)
    onChange({ kind: 'new', file })
  }

  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={inputId}>Imagen representativa (opcional)</Label>

      <div className="relative h-36 w-full overflow-hidden rounded-md border border-border bg-muted">
        {shownUrl ? (
          <img
            src={shownUrl}
            alt={value.kind === 'new' ? 'Vista previa de la imagen elegida' : 'Imagen actual del destino'}
            className="h-full w-full object-cover"
          />
        ) : (
          <div className="flex h-full items-center justify-center px-4 text-center text-caption text-muted-foreground">
            {value.kind === 'removed' ? 'La imagen se quitará al guardar.' : 'Sin imagen: la app muestra un fondo neutro.'}
          </div>
        )}
        {uploading && (
          <div className="absolute inset-x-0 bottom-0 bg-surface/90 px-3 py-2" role="status" aria-live="polite">
            <div className="mb-1 text-caption">Subiendo imagen… {progress}%</div>
            <div
              className="h-1.5 w-full overflow-hidden rounded-full bg-muted"
              role="progressbar"
              aria-label="Progreso de la subida"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={progress}
            >
              <div className="h-full bg-primary transition-[width]" style={{ width: `${progress}%` }} />
            </div>
          </div>
        )}
      </div>

      <input
        ref={inputRef}
        id={inputId}
        type="file"
        accept={ACCEPTED_IMAGE_TYPES.join(',')}
        className="sr-only"
        disabled={disabled}
        aria-describedby={validationError ? `${hintId} ${errorId}` : hintId}
        aria-invalid={validationError ? true : undefined}
        onChange={(event) => {
          onFileSelected(event.target.files?.[0])
          // Permite volver a elegir el mismo archivo después de quitarlo.
          event.target.value = ''
        }}
      />

      <div className="flex flex-wrap gap-2">
        <Button type="button" variant="outline" size="sm" disabled={disabled} onClick={() => inputRef.current?.click()}>
          <ImagePlus className="h-3.5 w-3.5" aria-hidden="true" />
          {hasImage ? 'Reemplazar imagen' : 'Seleccionar imagen'}
        </Button>
        {hasImage && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={disabled}
            onClick={() => {
              setValidationError(null)
              onChange(currentUrl ? { kind: 'removed' } : { kind: 'current' })
            }}
          >
            <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
            Quitar imagen
          </Button>
        )}
        {currentUrl && value.kind !== 'current' && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={disabled}
            onClick={() => {
              setValidationError(null)
              onChange({ kind: 'current' })
            }}
          >
            <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
            Mantener la actual
          </Button>
        )}
      </div>

      <p id={hintId} className="text-xs text-muted-foreground">
        {value.kind === 'new'
          ? `${value.file.name} · ${formatFileSize(value.file.size)}. Se sube al guardar.`
          : 'JPG, PNG o WebP. Máximo 5 MB.'}
      </p>
      {validationError && (
        <p id={errorId} role="alert" className="text-xs text-destructive">
          {validationError}
        </p>
      )}
    </div>
  )
}

/**
 * URL local (blob:) para previsualizar un archivo sin subirlo. Se crea en el momento de elegirlo —no en un
 * efecto— y se libera al elegir otro o al cerrar el diálogo.
 */
function usePreviewUrl() {
  const current = useRef<{ file: File; url: string } | null>(null)
  const [preview, setPreview] = useState<{ file: File; url: string } | null>(null)

  useEffect(() => {
    const ref = current
    return () => {
      if (ref.current) URL.revokeObjectURL(ref.current.url)
    }
  }, [])

  const createFor = (file: File) => {
    if (current.current) URL.revokeObjectURL(current.current.url)
    const next = { file, url: URL.createObjectURL(file) }
    current.current = next
    setPreview(next)
  }

  const urlFor = (file: File | null) => (file && preview?.file === file ? preview.url : null)

  return { createFor, urlFor }
}
