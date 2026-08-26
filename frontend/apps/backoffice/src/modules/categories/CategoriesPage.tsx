import { zodResolver } from '@hookform/resolvers/zod'
import type { CategoryResponse } from '@turisclick/api-client'
import { Pencil, Tags, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { getErrorMessage } from '@/lib/errors'
import { useCategories, useCreateCategory, useDeleteCategory, useUpdateCategory } from './api'

const schema = z.object({
  name: z.string().min(2).max(100),
  description: z.string().max(500).optional().or(z.literal('')),
})
type FormValues = z.infer<typeof schema>

export function CategoriesPage() {
  const { data: categories = [], isLoading } = useCategories()
  const createMutation = useCreateCategory()
  const deleteMutation = useDeleteCategory()
  const [createError, setCreateError] = useState<string | null>(null)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [editing, setEditing] = useState<CategoryResponse | null>(null)

  const form = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { name: '', description: '' } })

  const onCreate = async (values: FormValues) => {
    setCreateError(null)
    try {
      await createMutation.mutateAsync({ name: values.name, description: values.description || undefined })
      form.reset()
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
      <PageHeader title="Categorías" description="UC-A-05." />

      <Card className="mb-6">
        <CardHeader>
          <CardTitle className="text-base">Nueva categoría</CardTitle>
        </CardHeader>
        <CardContent>
          <form className="flex flex-wrap items-end gap-4" onSubmit={form.handleSubmit(onCreate)} noValidate>
            <div className="flex flex-col gap-1.5">
              <Label>Nombre</Label>
              <Input {...form.register('name')} className="w-56" />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label>Descripción (opcional)</Label>
              <Input {...form.register('description')} className="w-72" />
            </div>
            <Button type="submit" disabled={form.formState.isSubmitting}>
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
              <TableHead>Descripción</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={3} />}
            {!isLoading && categories.length === 0 && (
              <TableEmptyRow colSpan={3} icon={Tags} title="No hay categorías todavía" description="Creá la primera con el formulario de arriba." />
            )}
            {categories.map((category) => (
              <TableRow key={category.id}>
                <TableCell className="font-medium">{category.name}</TableCell>
                <TableCell className="text-muted-foreground">{category.description ?? '—'}</TableCell>
                <TableCell className="text-right">
                  <div className="flex justify-end gap-2">
                    <Button variant="outline" size="sm" onClick={() => setEditing(category)}>
                      <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                      Editar
                    </Button>
                    <Button variant="destructive" size="sm" onClick={() => void onDelete(category.id!)}>
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

      <EditDialog category={editing} onClose={() => setEditing(null)} />
    </div>
  )
}

function EditDialog({ category, onClose }: { category: CategoryResponse | null; onClose: () => void }) {
  return (
    <Dialog
      open={!!category}
      onOpenChange={(open) => {
        if (!open) onClose()
      }}
    >
      {category && <EditDialogContent key={category.id} category={category} onClose={onClose} />}
    </Dialog>
  )
}

function EditDialogContent({ category, onClose }: { category: CategoryResponse; onClose: () => void }) {
  const updateMutation = useUpdateCategory()
  const [name, setName] = useState(category.name ?? '')
  const [description, setDescription] = useState(category.description ?? '')
  const [error, setError] = useState<string | null>(null)

  const onSave = async () => {
    setError(null)
    try {
      await updateMutation.mutateAsync({ id: category.id!, body: { name, description: description || undefined } })
      onClose()
    } catch (err) {
      setError(getErrorMessage(err))
    }
  }

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>Editar categoría</DialogTitle>
      </DialogHeader>
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <Label>Nombre</Label>
          <Input value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label>Descripción</Label>
          <Input value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
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
