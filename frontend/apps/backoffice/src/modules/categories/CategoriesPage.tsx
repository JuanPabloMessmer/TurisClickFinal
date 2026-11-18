import { zodResolver } from '@hookform/resolvers/zod'
import type { CategoryResponse } from '@turisclick/api-client'
import { LIMITS, applyServerErrors, categoryNameSchema, plainTextSchema, singleLine } from '@turisclick/validation'
import { Pencil, Plus, Tags, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { FormField } from '@/components/form/FormField'
import { fieldAria } from '@/components/form/fieldAria'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { getErrorMessage } from '@/lib/errors'
import { useCategories, useCreateCategory, useDeleteCategory, useUpdateCategory } from './api'

type DialogState = { mode: 'create' } | { mode: 'edit'; category: CategoryResponse } | null

// Reglas R-07 y R-08 (docs/auditoria-validaciones.md): las mismas que valida /api/admin/categories.
const schema = z.object({
  name: categoryNameSchema(),
  description: plainTextSchema('La descripción', LIMITS.categoryDescriptionMax),
})

type FormValues = z.infer<typeof schema>

const FIELDS = ['name', 'description'] as const satisfies readonly (keyof FormValues)[]

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
        description="Los intereses con los que el viajero filtra el catálogo: gastronomía, naturaleza, cultura, aventura."
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
  const [error, setError] = useState<string | null>(null)
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    mode: 'onTouched',
    defaultValues: { name: category?.name ?? '', description: category?.description ?? '' },
  })
  const errors = form.formState.errors
  const name = useWatch({ control: form.control, name: 'name' }) ?? ''
  const description = useWatch({ control: form.control, name: 'description' }) ?? ''

  const isPending = createMutation.isPending || updateMutation.isPending

  const onSave = async (values: FormValues) => {
    setError(null)
    const body = { name: singleLine(values.name), description: singleLine(values.description) || undefined }
    try {
      if (isEdit) {
        await updateMutation.mutateAsync({ id: category.id!, body })
      } else {
        await createMutation.mutateAsync(body)
      }
      onClose()
    } catch (err) {
      setError(applyServerErrors(err, form.setError, FIELDS))
    }
  }

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>{isEdit ? 'Editar categoría' : 'Crear categoría'}</DialogTitle>
      </DialogHeader>
      <form id="category-form" className="flex flex-col gap-4" onSubmit={form.handleSubmit(onSave)} noValidate>
        <FormField
          id="category-name"
          label="Nombre"
          error={errors.name?.message}
          hint="De 3 a 40 caracteres. Puede llevar números, como en «Turismo 4x4»."
          counter={{ length: name.length, max: LIMITS.categoryNameMax }}
        >
          <Input {...fieldAria('category-name', errors.name?.message, true)} autoFocus {...form.register('name')} />
        </FormField>
        <FormField
          id="category-description"
          label="Descripción (opcional)"
          error={errors.description?.message}
          counter={{ length: description.length, max: LIMITS.categoryDescriptionMax }}
        >
          <Input {...fieldAria('category-description', errors.description?.message)} {...form.register('description')} />
        </FormField>
      </form>
      {error && (
        <Alert variant="destructive" className="mt-2">
          {error}
        </Alert>
      )}
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" form="category-form" disabled={isPending}>
          Guardar
        </Button>
      </DialogFooter>
    </DialogContent>
  )
}
