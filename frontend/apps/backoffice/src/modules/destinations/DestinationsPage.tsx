import { zodResolver } from '@hookform/resolvers/zod'
import type { DestinationResponse } from '@turisclick/api-client'
import { MapPinned, Pencil, Plus, Trash2 } from 'lucide-react'
import { useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { getErrorMessage } from '@/lib/errors'
import { useCreateDestination, useDeleteDestination, useDestinations, useUpdateDestination } from './api'
import { DestinationImageField } from './DestinationImageField'
import type { ImageSelection } from './imageFile'
import { useDestinationImageUpload } from './useDestinationImageUpload'

const createSchema = z.object({
  name: z.string().min(2).max(150),
  type: z.enum(['COUNTRY', 'REGION', 'CITY']),
  parentId: z.string().optional(),
})
type CreateFormValues = z.infer<typeof createSchema>

const parentTypeFor: Record<CreateFormValues['type'], 'COUNTRY' | 'REGION' | null> = {
  COUNTRY: null,
  REGION: 'COUNTRY',
  CITY: 'REGION',
}

const typeLabel: Record<string, string> = { COUNTRY: 'País', REGION: 'Región', CITY: 'Ciudad' }

export function DestinationsPage() {
  const { data: destinations = [], isLoading } = useDestinations()
  const deleteMutation = useDeleteDestination()
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<DestinationResponse | null>(null)

  const onDelete = async (id: string) => {
    setDeleteError(null)
    try {
      await deleteMutation.mutateAsync(id)
    } catch (error) {
      setDeleteError(getErrorMessage(error))
    }
  }

  return (
    <div>
      <PageHeader
        title="Destinos"
        description="Los países, regiones y ciudades donde se puede vender en TurisClick. Cada experiencia y cada paquete se publica en una ciudad."
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus className="h-4 w-4" aria-hidden="true" />
            Crear destino
          </Button>
        }
      />

      {deleteError && (
        <Alert variant="destructive" className="mb-4">
          {deleteError}
        </Alert>
      )}

      <Card>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-20">Imagen</TableHead>
              <TableHead>Nombre</TableHead>
              <TableHead>Tipo</TableHead>
              <TableHead>Padre</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={5} />}
            {!isLoading && destinations.length === 0 && (
              <TableEmptyRow colSpan={5} icon={MapPinned} title="No hay destinos todavía" description="Usá 'Crear destino' para dar de alta el primero." />
            )}
            {destinations.map((destination) => (
              <TableRow key={destination.id}>
                <TableCell>
                  {destination.imageUrl ? (
                    <img src={destination.imageUrl} alt="" loading="lazy" className="h-10 w-16 rounded object-cover" />
                  ) : (
                    <div className="h-10 w-16 rounded bg-muted" aria-label="Sin imagen" />
                  )}
                </TableCell>
                <TableCell className="font-medium">{destination.name}</TableCell>
                <TableCell>
                  <Badge variant="neutral">{typeLabel[destination.type ?? ''] ?? destination.type}</Badge>
                </TableCell>
                <TableCell className="text-muted-foreground">{destination.parentName ?? '—'}</TableCell>
                <TableCell className="text-right">
                  <div className="flex justify-end gap-2">
                    <Button variant="outline" size="sm" onClick={() => setEditing(destination)}>
                      <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                      Editar
                    </Button>
                    <Button variant="destructive" size="sm" onClick={() => void onDelete(destination.id!)}>
                      <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                      Eliminar
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>

      <CreateDialog open={creating} onClose={() => setCreating(false)} destinations={destinations} />
      <EditDialog destination={editing} onClose={() => setEditing(null)} />
    </div>
  )
}

/**
 * Mientras se sube la imagen o se guarda, el diálogo no se cierra (Esc, clic afuera o la X): cerrar a mitad de
 * camino dejaría una imagen subida sin destino, o un guardado que el usuario cree cancelado.
 */
function useBusyGuard(onClose: () => void) {
  const [busy, setBusy] = useState(false)
  const onOpenChange = (open: boolean) => {
    if (!open && !busy) onClose()
  }
  return { setBusy, onOpenChange }
}

/**
 * Evita el doble envío: el estado de React tarda un render en deshabilitar el botón, y un doble clic rápido
 * alcanza a disparar dos subidas y dos altas. La referencia corta el segundo envío en el mismo instante.
 */
function useSingleFlight(onBusyChange: (busy: boolean) => void) {
  const inFlight = useRef(false)
  const [running, setRunning] = useState(false)
  const run = async (task: () => Promise<void>) => {
    if (inFlight.current) return
    inFlight.current = true
    setRunning(true)
    onBusyChange(true)
    try {
      await task()
    } finally {
      inFlight.current = false
      setRunning(false)
      onBusyChange(false)
    }
  }
  return { running, run }
}

function CreateDialog({ open, onClose, destinations }: { open: boolean; onClose: () => void; destinations: DestinationResponse[] }) {
  const { setBusy, onOpenChange } = useBusyGuard(onClose)
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* El unmount/mount natural del "open &&" ya resetea el form cada vez que se vuelve a abrir. */}
      {open && <CreateDialogContent onClose={onClose} onBusyChange={setBusy} destinations={destinations} />}
    </Dialog>
  )
}

function CreateDialogContent({
  onClose,
  onBusyChange,
  destinations,
}: {
  onClose: () => void
  onBusyChange: (busy: boolean) => void
  destinations: DestinationResponse[]
}) {
  const createMutation = useCreateDestination()
  const { progress, resolveImageUrl } = useDestinationImageUpload()
  const { running, run } = useSingleFlight(onBusyChange)
  const [image, setImage] = useState<ImageSelection>({ kind: 'current' })
  const [submitError, setSubmitError] = useState<string | null>(null)

  const form = useForm<CreateFormValues>({
    resolver: zodResolver(createSchema),
    defaultValues: { name: '', type: 'COUNTRY', parentId: undefined },
  })
  const selectedType = form.watch('type')
  const requiredParentType = parentTypeFor[selectedType]
  const parentOptions = destinations.filter((d) => d.type === requiredParentType)

  const onSubmit = (values: CreateFormValues) =>
    run(async () => {
      setSubmitError(null)
      try {
        const imageUrl = await resolveImageUrl(image)
        await createMutation.mutateAsync({
          name: values.name,
          type: values.type,
          parentId: requiredParentType ? values.parentId : undefined,
          imageUrl: imageUrl ?? undefined,
        })
        onClose()
      } catch (error) {
        setSubmitError(getErrorMessage(error))
      }
    })

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>Crear destino</DialogTitle>
      </DialogHeader>
      <form className="flex flex-col gap-4" onSubmit={form.handleSubmit(onSubmit)} noValidate>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="destination-name">Nombre</Label>
          <Input id="destination-name" {...form.register('name')} autoFocus disabled={running} />
          {form.formState.errors.name && <p className="text-xs text-destructive">{form.formState.errors.name.message}</p>}
        </div>
        <div className="flex flex-col gap-1.5">
          <Label>Tipo</Label>
          <Select
            value={selectedType}
            disabled={running}
            onValueChange={(value) => {
              form.setValue('type', value as CreateFormValues['type'])
              form.setValue('parentId', undefined)
            }}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="COUNTRY">País</SelectItem>
              <SelectItem value="REGION">Región</SelectItem>
              <SelectItem value="CITY">Ciudad</SelectItem>
            </SelectContent>
          </Select>
        </div>
        {requiredParentType && (
          <div className="flex flex-col gap-1.5">
            <Label>Padre ({requiredParentType === 'COUNTRY' ? 'País' : 'Región'})</Label>
            <Select value={form.watch('parentId')} disabled={running} onValueChange={(value) => form.setValue('parentId', value)}>
              <SelectTrigger>
                <SelectValue placeholder="Seleccioná el padre" />
              </SelectTrigger>
              <SelectContent>
                {parentOptions.map((option) => (
                  <SelectItem key={option.id} value={option.id!}>
                    {option.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}
        <DestinationImageField value={image} onChange={setImage} progress={progress} disabled={running} />
        {submitError && <Alert variant="destructive">{submitError}</Alert>}
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose} disabled={running}>
            Cancelar
          </Button>
          <Button type="submit" loading={running} disabled={!!requiredParentType && !form.watch('parentId')}>
            {progress !== null ? 'Subiendo imagen…' : running ? 'Guardando…' : 'Crear'}
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  )
}

function EditDialog({ destination, onClose }: { destination: DestinationResponse | null; onClose: () => void }) {
  const { setBusy, onOpenChange } = useBusyGuard(onClose)
  return (
    <Dialog open={!!destination} onOpenChange={onOpenChange}>
      {/* key fuerza un remount por cada destino distinto, así el estado local arranca limpio. */}
      {destination && <EditDialogContent key={destination.id} destination={destination} onClose={onClose} onBusyChange={setBusy} />}
    </Dialog>
  )
}

function EditDialogContent({
  destination,
  onClose,
  onBusyChange,
}: {
  destination: DestinationResponse
  onClose: () => void
  onBusyChange: (busy: boolean) => void
}) {
  const updateMutation = useUpdateDestination()
  const { progress, resolveImageUrl } = useDestinationImageUpload()
  const { running, run } = useSingleFlight(onBusyChange)
  const [name, setName] = useState(destination.name ?? '')
  // El PUT reemplaza nombre e imagen juntos: por defecto se conserva la imagen actual (sea una URL externa
  // previa o una subida), para no borrarla al sólo renombrar.
  const [image, setImage] = useState<ImageSelection>({ kind: 'current' })
  const [error, setError] = useState<string | null>(null)

  const onSave = () =>
    run(async () => {
      setError(null)
      try {
        const imageUrl = await resolveImageUrl(image, destination.imageUrl)
        await updateMutation.mutateAsync({ id: destination.id!, body: { name, imageUrl } })
        onClose()
      } catch (err) {
        setError(getErrorMessage(err))
      }
    })

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>Editar destino</DialogTitle>
      </DialogHeader>
      <form
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault()
          void onSave()
        }}
        noValidate
      >
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="destination-edit-name">Nombre</Label>
          <Input id="destination-edit-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus disabled={running} />
          <p className="text-xs text-muted-foreground">Tipo y padre son estructurales y no se editan.</p>
        </div>
        <DestinationImageField
          currentUrl={destination.imageUrl}
          value={image}
          onChange={setImage}
          progress={progress}
          disabled={running}
        />
        {error && <Alert variant="destructive">{error}</Alert>}
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose} disabled={running}>
            Cancelar
          </Button>
          <Button type="submit" loading={running}>
            {progress !== null ? 'Subiendo imagen…' : running ? 'Guardando…' : 'Guardar'}
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  )
}
