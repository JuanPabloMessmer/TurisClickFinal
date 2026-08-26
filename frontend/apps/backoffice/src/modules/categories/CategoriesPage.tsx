import type { CategoryResponse } from '@turisclick/api-client'
import { Pencil, Plus, Tags, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { getErrorMessage } from '@/lib/errors'
import { useCategories, useCreateCategory, useDeleteCategory, useUpdateCategory } from './api'

type DialogState = { mode: 'create' } | { mode: 'edit'; category: CategoryResponse } | null

export function CategoriesPage() {
  const { data: categories = [], isLoading } = useCategories()
  const deleteMutation = useDeleteCategory()
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [dialog, setDialog] = useState<DialogState>(null)

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
        title="Categorías"
        description="UC-A-05."
        actions={
          <Button onClick={() => setDialog({ mode: 'create' })}>
            <Plus className="h-4 w-4" aria-hidden="true" />
            Crear categoría
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
              <TableHead>Descripción</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={3} />}
            {!isLoading && categories.length === 0 && (
              <TableEmptyRow colSpan={3} icon={Tags} title="No hay categorías todavía" description="Usá 'Crear categoría' para dar de alta la primera." />
            )}
            {categories.map((category) => (
              <TableRow key={category.id}>
                <TableCell className="font-medium">{category.name}</TableCell>
                <TableCell className="text-muted-foreground">{category.description ?? '—'}</TableCell>
                <TableCell className="text-right">
                  <div className="flex justify-end gap-2">
                    <Button variant="outline" size="sm" onClick={() => setDialog({ mode: 'edit', category })}>
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

      <CategoryDialog state={dialog} onClose={() => setDialog(null)} />
    </div>
  )
}

function CategoryDialog({ state, onClose }: { state: DialogState; onClose: () => void }) {
  return (
    <Dialog
      open={!!state}
      onOpenChange={(open) => {
        if (!open) onClose()
      }}
    >
      {state?.mode === 'create' && <CategoryDialogContent key="create" onClose={onClose} />}
      {state?.mode === 'edit' && <CategoryDialogContent key={state.category.id} category={state.category} onClose={onClose} />}
    </Dialog>
  )
}

function CategoryDialogContent({ category, onClose }: { category?: CategoryResponse; onClose: () => void }) {
  const isEdit = !!category
  const createMutation = useCreateCategory()
  const updateMutation = useUpdateCategory()
  const [name, setName] = useState(category?.name ?? '')
  const [description, setDescription] = useState(category?.description ?? '')
  const [error, setError] = useState<string | null>(null)

  const isPending = createMutation.isPending || updateMutation.isPending

  const onSave = async () => {
    setError(null)
    try {
      if (isEdit) {
        await updateMutation.mutateAsync({ id: category.id!, body: { name, description: description || undefined } })
      } else {
        await createMutation.mutateAsync({ name, description: description || undefined })
      }
      onClose()
    } catch (err) {
      setError(getErrorMessage(err))
    }
  }

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>{isEdit ? 'Editar categoría' : 'Crear categoría'}</DialogTitle>
      </DialogHeader>
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <Label>Nombre</Label>
          <Input value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label>Descripción (opcional)</Label>
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
        <Button onClick={() => void onSave()} disabled={isPending || name.trim().length < 2}>
          Guardar
        </Button>
      </DialogFooter>
    </DialogContent>
  )
}
