import { zodResolver } from '@hookform/resolvers/zod'
import type { DestinationResponse } from '@turisclick/api-client'
import { MapPinned, Pencil, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
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
  const createMutation = useCreateDestination()
  const deleteMutation = useDeleteDestination()
  const [createError, setCreateError] = useState<string | null>(null)
  const [renaming, setRenaming] = useState<DestinationResponse | null>(null)
  const [deleteError, setDeleteError] = useState<string | null>(null)

  const form = useForm<CreateFormValues>({
    resolver: zodResolver(createSchema),
    defaultValues: { name: '', type: 'COUNTRY', parentId: undefined },
  })
  const selectedType = form.watch('type')
  const requiredParentType = parentTypeFor[selectedType]
  const parentOptions = destinations.filter((d) => d.type === requiredParentType)

  const onCreate = async (values: CreateFormValues) => {
    setCreateError(null)
    try {
      await createMutation.mutateAsync({
        name: values.name,
        type: values.type,
        parentId: requiredParentType ? values.parentId : undefined,
      })
      form.reset({ name: '', type: values.type, parentId: undefined })
    } catch (error) {
      setCreateError(getErrorMessage(error))
    }
  }

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
      <PageHeader title="Destinos" description="UC-A-04 — jerarquía País → Región → Ciudad." />

      <Card className="mb-6">
        <CardHeader>
          <CardTitle className="text-base">Nuevo destino</CardTitle>
        </CardHeader>
        <CardContent>
          <form className="flex flex-wrap items-end gap-4" onSubmit={form.handleSubmit(onCreate)} noValidate>
            <div className="flex flex-col gap-1.5">
              <Label>Nombre</Label>
              <Input {...form.register('name')} className="w-56" />
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
                <SelectTrigger className="w-40">
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
                  <SelectTrigger className="w-56">
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
            <Button type="submit" disabled={form.formState.isSubmitting || (!!requiredParentType && !form.watch('parentId'))}>
              Crear
            </Button>
          </form>
          {createError && (
            <Alert variant="destructive" className="mt-4">
              {createError}
            </Alert>
          )}
        </CardContent>
      </Card>

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
              <TableEmptyRow colSpan={4} icon={MapPinned} title="No hay destinos todavía" description="Creá el primero con el formulario de arriba." />
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
                      Renombrar
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

      <RenameDialog destination={renaming} onClose={() => setRenaming(null)} />
    </div>
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
        <DialogTitle>Renombrar destino</DialogTitle>
      </DialogHeader>
      <div className="flex flex-col gap-1.5">
        <Label>Nombre</Label>
        <Input value={name} onChange={(e) => setName(e.target.value)} />
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
