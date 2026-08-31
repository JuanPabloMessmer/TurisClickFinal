import { zodResolver } from '@hookform/resolvers/zod'
import type { ExperienceSummaryResponse } from '@turisclick/api-client'
import { GripVertical, ImagePlus, Plus, Star, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { useNavigate, useParams } from 'react-router-dom'
import { z } from 'zod'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { KNOWN_CURRENCIES } from '@/modules/experiences/currencies'
import { useMyExperiences } from '@/modules/experiences/api'
import { getErrorMessage } from '@/lib/errors'
import { useCityDestinations, useCreatePackage, useMyPackage, useUpdatePackage } from './api'

const packageItemSchema = z
  .object({
    dayNumber: z.coerce.number().int().min(1),
    sortOrder: z.coerce.number().int().min(0),
    kind: z.enum(['EXPERIENCE_REFERENCE', 'DESCRIPTIVE']),
    experienceId: z.string().optional().or(z.literal('')),
    title: z.string().max(200).optional().or(z.literal('')),
    description: z.string().optional().or(z.literal('')),
  })
  .refine((item) => item.kind !== 'EXPERIENCE_REFERENCE' || !!item.experienceId, {
    message: 'Elegí una experiencia.',
    path: ['experienceId'],
  })
  .refine((item) => item.kind !== 'DESCRIPTIVE' || !!item.title, {
    message: 'El título es obligatorio para un ítem descriptivo.',
    path: ['title'],
  })

const packageImageSchema = z.object({
  url: z.string().url('Ingresá una URL válida.'),
  isCover: z.boolean(),
})

const schema = z.object({
  title: z.string().min(3).max(200),
  description: z.string().min(10),
  destinationId: z.string().min(1, 'Elegí un destino.'),
  conditionsText: z.string().optional().or(z.literal('')),
  durationDays: z.coerce.number().int().min(1).max(90),
  price: z.coerce.number().min(0),
  currency: z.string().min(1, 'Elegí una moneda.'),
  items: z.array(packageItemSchema),
  images: z.array(packageImageSchema),
})
// z.coerce hace que el tipo de entrada (lo que RHF maneja en los inputs) difiera del de salida
// (lo que recibe el submit ya validado) — RHF necesita los dos generics para no chocar de tipos.
type FormInput = z.input<typeof schema>
type FormOutput = z.output<typeof schema>

export function PackageFormPage() {
  const { id } = useParams<{ id: string }>()
  const isEdit = !!id
  const navigate = useNavigate()
  const [submitError, setSubmitError] = useState<string | null>(null)

  const { data: cities = [], isLoading: loadingCities } = useCityDestinations()
  const { data: experiencesData } = useMyExperiences()
  const experiences = experiencesData?.items ?? []
  const { data: pkg, isLoading: loadingPackage } = useMyPackage(id)
  const createMutation = useCreatePackage()
  const updateMutation = useUpdatePackage()

  const form = useForm<FormInput, unknown, FormOutput>({
    resolver: zodResolver(schema),
    values: pkg
      ? {
          title: pkg.title ?? '',
          description: pkg.description ?? '',
          destinationId: pkg.destinationId ?? '',
          conditionsText: pkg.conditionsText ?? '',
          durationDays: pkg.durationDays ?? 1,
          price: pkg.price ?? 0,
          currency: pkg.currency ?? '',
          items: (pkg.items ?? []).map((item) => ({
            dayNumber: item.dayNumber ?? 1,
            sortOrder: item.sortOrder ?? 0,
            kind: (item.kind as 'EXPERIENCE_REFERENCE' | 'DESCRIPTIVE') ?? 'DESCRIPTIVE',
            experienceId: item.experienceId ?? '',
            title: item.title ?? '',
            description: item.description ?? '',
          })),
          images: (pkg.images ?? []).map((image) => ({ url: image.url ?? '', isCover: !!image.isCover })),
        }
      : undefined,
    defaultValues: {
      title: '',
      description: '',
      destinationId: '',
      durationDays: 3,
      price: 0,
      currency: 'USD',
      items: [],
      images: [],
    },
  })

  const itemsArray = useFieldArray({ control: form.control, name: 'items' })
  const imagesArray = useFieldArray({ control: form.control, name: 'images' })

  if (isEdit && loadingPackage) return <FullScreenSpinner />

  const durationDays = Math.max(1, Math.min(90, Number(form.watch('durationDays')) || 1))
  const days = Array.from({ length: durationDays }, (_, i) => i + 1)

  const addItem = (day: number) => {
    const itemsOnDay = itemsArray.fields.filter((_, i) => Number(form.getValues(`items.${i}.dayNumber`)) === day)
    itemsArray.append({ dayNumber: day, sortOrder: itemsOnDay.length, kind: 'DESCRIPTIVE', experienceId: '', title: '', description: '' })
  }

  const onSubmit = async (values: FormOutput) => {
    setSubmitError(null)
    const body = {
      title: values.title,
      description: values.description,
      destinationId: values.destinationId,
      categoryIds: [] as string[],
      conditionsText: values.conditionsText || undefined,
      durationDays: values.durationDays,
      price: values.price,
      currency: values.currency,
      items: values.items.map((item) => ({
        dayNumber: item.dayNumber,
        sortOrder: item.sortOrder,
        kind: item.kind,
        experienceId: item.kind === 'EXPERIENCE_REFERENCE' ? item.experienceId || undefined : undefined,
        title: item.title || undefined,
        description: item.description || undefined,
      })),
      images: values.images.map((image) => ({ url: image.url, isCover: image.isCover })),
    }
    try {
      if (isEdit) {
        await updateMutation.mutateAsync({ id, body })
      } else {
        await createMutation.mutateAsync(body)
      }
      navigate('/provider/packages')
    } catch (error) {
      setSubmitError(getErrorMessage(error))
    }
  }

  const setOnlyCover = (globalIndex: number) => {
    imagesArray.fields.forEach((_, i) => form.setValue(`images.${i}.isCover`, i === globalIndex, { shouldDirty: true }))
  }

  return (
    <div className="max-w-3xl">
      <PageHeader
        title={isEdit ? 'Editar paquete' : 'Nuevo paquete'}
        description="UC-P-07/08 — datos generales, itinerario día a día, galería. La disponibilidad se configura aparte."
      />
      <Alert variant="info" className="mb-6">
        Las categorías todavía no están disponibles desde el Backoffice — se crea/edita sin categorías (mismo criterio que Experiencias).
      </Alert>

      <form className="flex flex-col gap-6" onSubmit={form.handleSubmit(onSubmit)} noValidate>
        {/* ---- Sección 1: Datos generales ---- */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Datos generales</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-col gap-1.5">
              <Label>Título</Label>
              <Input placeholder='ej. "Uyuni Experience — 3 días / 2 noches"' {...form.register('title')} />
              {form.formState.errors.title && <p className="text-xs text-destructive">{form.formState.errors.title.message}</p>}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label>Descripción</Label>
              <Textarea rows={3} {...form.register('description')} />
              {form.formState.errors.description && (
                <p className="text-xs text-destructive">{form.formState.errors.description.message}</p>
              )}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label>Destino principal (ciudad)</Label>
              <Select
                value={form.watch('destinationId')}
                onValueChange={(value) => {
                  // Radix Select puede disparar onValueChange("") una vez justo después de montar,
                  // antes de que sus SelectItem terminen de registrarse — aplicar ese valor vacío
                  // pisaba el destinationId ya cargado en edición (bug encontrado en Oleada 4). No
                  // hay ninguna opción "vacía" real en esta lista, así que ignorar "" es seguro.
                  if (value) form.setValue('destinationId', value, { shouldValidate: true })
                }}
                disabled={loadingCities}
              >
                <SelectTrigger>
                  <SelectValue placeholder="Seleccioná una ciudad" />
                </SelectTrigger>
                <SelectContent>
                  {cities.map((city) => (
                    <SelectItem key={city.id} value={city.id!}>
                      {city.name} {city.parentName ? `(${city.parentName})` : ''}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {form.formState.errors.destinationId && (
                <p className="text-xs text-destructive">{form.formState.errors.destinationId.message}</p>
              )}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label>Condiciones / qué incluye-excluye (opcional)</Label>
              <Textarea rows={2} {...form.register('conditionsText')} />
            </div>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <div className="flex flex-col gap-1.5">
                <Label>Duración (días)</Label>
                <Input type="number" min="1" max="90" {...form.register('durationDays')} />
                {form.formState.errors.durationDays && (
                  <p className="text-xs text-destructive">{form.formState.errors.durationDays.message}</p>
                )}
              </div>
              <div className="flex flex-col gap-1.5">
                <Label>Precio del paquete</Label>
                <Input type="number" step="0.01" min="0" {...form.register('price')} />
                <p className="text-xs text-muted-foreground">No se calcula sumando los ítems — es el precio comercial que fijás.</p>
              </div>
              <div className="flex flex-col gap-1.5">
                <Label>Moneda</Label>
                <Select
                  value={form.watch('currency')}
                  onValueChange={(value) => {
                    // Mismo guard que destinationId — ver comentario arriba.
                    if (value) form.setValue('currency', value, { shouldValidate: true })
                  }}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {KNOWN_CURRENCIES.map((currency) => (
                      <SelectItem key={currency} value={currency}>
                        {currency}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* ---- Sección 2: Itinerario por día ---- */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Itinerario</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-5">
            <p className="text-xs text-muted-foreground">
              Cada día puede combinar experiencias reales de tu empresa (reutilizables, vendibles también por separado) con ítems
              puramente descriptivos (desayuno, traslado, tiempo libre).
            </p>
            {days.map((day) => (
              <DaySection
                key={day}
                day={day}
                form={form}
                itemsArray={itemsArray}
                experiences={experiences}
                onAddItem={() => addItem(day)}
              />
            ))}
          </CardContent>
        </Card>

        {/* ---- Sección 3: Galería ---- */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Galería de imágenes</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            {imagesArray.fields.length === 0 && <p className="text-sm text-muted-foreground">Todavía no agregaste imágenes.</p>}
            {imagesArray.fields.map((field, index) => (
              <div key={field.id} className="flex items-end gap-2">
                <div className="flex flex-1 flex-col gap-1.5">
                  <Label>URL de imagen {index + 1}</Label>
                  <Input placeholder="https://…" {...form.register(`images.${index}.url`)} />
                  {form.formState.errors.images?.[index]?.url && (
                    <p className="text-xs text-destructive">{form.formState.errors.images[index]?.url?.message}</p>
                  )}
                </div>
                <Button
                  type="button"
                  variant={form.watch(`images.${index}.isCover`) ? 'default' : 'outline'}
                  size="sm"
                  onClick={() => setOnlyCover(index)}
                  title="Marcar como portada"
                >
                  <Star className="h-3.5 w-3.5" aria-hidden="true" />
                  {form.watch(`images.${index}.isCover`) ? 'Portada' : 'Marcar portada'}
                </Button>
                <Button type="button" variant="destructive" size="sm" onClick={() => imagesArray.remove(index)}>
                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                </Button>
              </div>
            ))}
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="self-start"
              onClick={() => imagesArray.append({ url: '', isCover: imagesArray.fields.length === 0 })}
            >
              <ImagePlus className="h-3.5 w-3.5" aria-hidden="true" />
              Agregar imagen
            </Button>
          </CardContent>
        </Card>

        {submitError && <Alert variant="destructive">{submitError}</Alert>}
        <Button type="submit" disabled={form.formState.isSubmitting} className="self-start">
          {isEdit ? 'Guardar cambios' : 'Crear paquete'}
        </Button>
      </form>
    </div>
  )
}

function DaySection({
  day,
  form,
  itemsArray,
  experiences,
  onAddItem,
}: {
  day: number
  form: ReturnType<typeof useForm<FormInput, unknown, FormOutput>>
  itemsArray: ReturnType<typeof useFieldArray<FormInput, 'items'>>
  experiences: ExperienceSummaryResponse[]
  onAddItem: () => void
}) {
  const indices = itemsArray.fields
    .map((field, index) => ({ field, index }))
    .filter(({ index }) => Number(form.watch(`items.${index}.dayNumber`)) === day)
    .sort((a, b) => Number(form.watch(`items.${a.index}.sortOrder`)) - Number(form.watch(`items.${b.index}.sortOrder`)))

  return (
    <div className="rounded-lg border border-border p-4">
      <div className="mb-3 flex items-center justify-between">
        <h3 className="text-sm font-semibold text-foreground">Día {day}</h3>
        <Button type="button" variant="outline" size="sm" onClick={onAddItem}>
          <Plus className="h-3.5 w-3.5" aria-hidden="true" />
          Agregar ítem
        </Button>
      </div>

      {indices.length === 0 && <p className="text-xs text-muted-foreground">Sin ítems todavía para este día.</p>}

      <div className="flex flex-col gap-3">
        {indices.map(({ index }) => {
          const kind = form.watch(`items.${index}.kind`)
          return (
            <div key={itemsArray.fields[index]!.id} className="flex flex-col gap-2 rounded-md bg-muted/40 p-3">
              <div className="flex items-center gap-2">
                <GripVertical className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
                <Select
                  value={kind}
                  onValueChange={(value) => {
                    // Mismo guard que el Select de destino (ver PackageFormPage arriba) — Radix puede
                    // disparar un onValueChange("") espurio justo al montar.
                    if (value) form.setValue(`items.${index}.kind`, value as 'EXPERIENCE_REFERENCE' | 'DESCRIPTIVE')
                  }}
                >
                  <SelectTrigger className="w-56">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="EXPERIENCE_REFERENCE">Experiencia existente</SelectItem>
                    <SelectItem value="DESCRIPTIVE">Elemento descriptivo</SelectItem>
                  </SelectContent>
                </Select>
                <Badge variant="neutral">Orden {Number(form.watch(`items.${index}.sortOrder`)) + 1}</Badge>
                <Button type="button" variant="ghost" size="sm" className="ml-auto" onClick={() => itemsArray.remove(index)}>
                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                </Button>
              </div>

              {kind === 'EXPERIENCE_REFERENCE' ? (
                <div className="flex flex-col gap-1.5">
                  <Select
                    value={form.watch(`items.${index}.experienceId`) || undefined}
                    onValueChange={(value) => {
                      // Mismo guard que el Select de destino (ver PackageFormPage arriba).
                      if (value) form.setValue(`items.${index}.experienceId`, value, { shouldValidate: true })
                    }}
                  >
                    <SelectTrigger>
                      <SelectValue placeholder="Elegí una experiencia propia" />
                    </SelectTrigger>
                    <SelectContent>
                      {experiences.map((experience) => (
                        <SelectItem key={experience.id} value={experience.id!}>
                          {experience.title}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  {form.formState.errors.items?.[index]?.experienceId && (
                    <p className="text-xs text-destructive">{form.formState.errors.items[index]?.experienceId?.message}</p>
                  )}
                </div>
              ) : (
                <div className="flex flex-col gap-2">
                  <div className="flex flex-col gap-1.5">
                    <Input placeholder="ej. Traslado al aeropuerto" {...form.register(`items.${index}.title`)} />
                    {form.formState.errors.items?.[index]?.title && (
                      <p className="text-xs text-destructive">{form.formState.errors.items[index]?.title?.message}</p>
                    )}
                  </div>
                  <Textarea rows={2} placeholder="Detalle opcional" {...form.register(`items.${index}.description`)} />
                </div>
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}
