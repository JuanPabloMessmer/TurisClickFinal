import { zodResolver } from '@hookform/resolvers/zod'
import type { DestinationResponse } from '@turisclick/api-client'
import { MapPinned, Pencil, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
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
  const [renaming, setRenaming] = useState<DestinationResponse | null>(null)

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
        description="UC-A-04 — jerarquía País → Región → Ciudad."
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
              <TableHead>Nombre</TableHead>
              <TableHead>Tipo</TableHead>
              <TableHead>Padre</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={4} />}
            {!isLoading && destinations.length === 0 && (
              <TableEmptyRow colSpan={4} icon={MapPinned} title="No hay destinos todavía" description="Usá 'Crear destino' para dar de alta el primero." />
            )}
            {destinations.map((destination) => (
              <TableRow key={destination.id}>
                <TableCell className="font-medium">{destination.name}</TableCell>
                <TableCell>
                  <Badge variant="neutral">{typeLabel[destination.type ?? ''] ?? destination.type}</Badge>
                </TableCell>
                <TableCell className="text-muted-foreground">{destination.parentName ?? '—'}</TableCell>
                <TableCell className="text-right">
                  <div className="flex justify-end gap-2">
                    <Button variant="outline" size="sm" onClick={() => setRenaming(destination)}>
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
      <RenameDialog destination={renaming} onClose={() => setRenaming(null)} />
    </div>
  )
}

function CreateDialog({ open, onClose, destinations }: { open: boolean; onClose: () => void; destinations: DestinationResponse[] }) {
  return (
    <Dialog
      open={open}
      onOpenChange={(open) => {
        if (!open) onClose()
      }}
    >
      {/* El unmount/mount natural del "open &&" ya resetea el form cada vez que se vuelve a abrir. */}
      {open && <CreateDialogContent onClose={onClose} destinations={destinations} />}
    </Dialog>
  )
}

function CreateDialogContent({
  onClose,
  destinations,
}: {
  onClose: () => void
  destinations: DestinationResponse[]
}) {
  const createMutation = useCreateDestination()
  const [submitError, setSubmitError] = useState<string | null>(null)

  const form = useForm<CreateFormValues>({
    resolver: zodResolver(createSchema),
    defaultValues: { name: '', type: 'COUNTRY', parentId: undefined },
  })
  const selectedType = form.watch('type')
  const requiredParentType = parentTypeFor[selectedType]
  const parentOptions = destinations.filter((d) => d.type === requiredParentType)

  const onSubmit = async (values: CreateFormValues) => {
    setSubmitError(null)
    try {
      await createMutation.mutateAsync({
        name: values.name,
        type: values.type,
        parentId: requiredParentType ? values.parentId : undefined,
      })
      onClose()
    } catch (error) {
      setSubmitError(getErrorMessage(error))
    }
  }

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>Crear destino</DialogTitle>
      </DialogHeader>
      <form className="flex flex-col gap-4" onSubmit={form.handleSubmit(onSubmit)} noValidate>
        <div className="flex flex-col gap-1.5">
          <Label>Nombre</Label>
          <Input {...form.register('name')} autoFocus />
          {form.formState.errors.name && <p className="text-xs text-destructive">{form.formState.errors.name.message}</p>}
        </div>
        <div className="flex flex-col gap-1.5">
          <Label>Tipo</Label>
          <Select
            value={selectedType}
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
            <Select value={form.watch('parentId')} onValueChange={(value) => form.setValue('parentId', value)}>
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
        {submitError && <Alert variant="destructive">{submitError}</Alert>}
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={form.formState.isSubmitting || (!!requiredParentType && !form.watch('parentId'))}>
            Crear
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  )
}

function RenameDialog({ destination, onClose }: { destination: DestinationResponse | null; onClose: () => void }) {
  return (
    <Dialog
      open={!!destination}
      onOpenChange={(open) => {
        if (!open) onClose()
      }}
    >
      {/* key fuerza un remount por cada destino distinto, así el estado local del input arranca limpio. */}
      {destination && <RenameDialogContent key={destination.id} destination={destination} onClose={onClose} />}
    </Dialog>
  )
}

function RenameDialogContent({ destination, onClose }: { destination: DestinationResponse; onClose: () => void }) {
  const updateMutation = useUpdateDestination()
  const [name, setName] = useState(destination.name ?? '')
  const [error, setError] = useState<string | null>(null)

  const onSave = async () => {
    setError(null)
    try {
      await updateMutation.mutateAsync({ id: destination.id!, body: { name } })
      onClose()
    } catch (err) {
      setError(getErrorMessage(err))
    }
  }

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>Editar destino</DialogTitle>
      </DialogHeader>
      <div className="flex flex-col gap-1.5">
        <Label>Nombre</Label>
        <Input value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        <p className="text-xs text-muted-foreground">Solo se puede editar el nombre — tipo y padre son estructurales.</p>
      </div>
      {error && (
        <Alert variant="destructive" className="mt-2">
          {error}
        </Alert>
      )}
      <DialogFooter>
        <Button variant="outline" onClick={onClose}>
          Cancelar
        </Button>
        <Button onClick={() => void onSave()} disabled={updateMutation.isPending}>
          Guardar
        </Button>
      </DialogFooter>
    </DialogContent>
  )
}
