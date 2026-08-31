import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate, useParams } from 'react-router-dom'
import { z } from 'zod'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { getErrorMessage } from '@/lib/errors'
import { useCityDestinations, useCreateExperience, useMyExperience, useUpdateExperience } from './api'
import { KNOWN_CURRENCIES } from './currencies'

const schema = z.object({
  title: z.string().min(3).max(200),
  description: z.string().min(10),
  destinationId: z.string().min(1, 'Elegí un destino.'),
  price: z.coerce.number().min(0),
  currency: z.string().min(1, 'Elegí una moneda.'),
  durationMinutes: z.coerce.number().int().min(1).optional().or(z.literal('')),
  durationLabel: z.string().max(100).optional().or(z.literal('')),
  includesText: z.string().optional().or(z.literal('')),
  excludesText: z.string().optional().or(z.literal('')),
  coverImageUrl: z.string().url('Ingresá una URL válida.').optional().or(z.literal('')),
})
// z.coerce hace que el tipo de entrada (lo que RHF maneja en los inputs) difiera del de salida
// (lo que recibe el submit ya validado) — RHF necesita los dos generics para no chocar de tipos.
type FormInput = z.input<typeof schema>
type FormOutput = z.output<typeof schema>

export function ExperienceFormPage() {
  const { id } = useParams<{ id: string }>()
  const isEdit = !!id
  const navigate = useNavigate()
  const [submitError, setSubmitError] = useState<string | null>(null)

  const { data: cities = [], isLoading: loadingCities } = useCityDestinations()
  const { data: experience, isLoading: loadingExperience } = useMyExperience(id)
  const createMutation = useCreateExperience()
  const updateMutation = useUpdateExperience()

  const form = useForm<FormInput, unknown, FormOutput>({
    resolver: zodResolver(schema),
    values: experience
      ? {
          title: experience.title ?? '',
          description: experience.description ?? '',
          destinationId: experience.destinationId ?? '',
          price: experience.price ?? 0,
          currency: experience.currency ?? '',
          durationMinutes: experience.durationMinutes ?? '',
          durationLabel: experience.durationLabel ?? '',
          includesText: experience.includesText ?? '',
          excludesText: experience.excludesText ?? '',
          coverImageUrl: experience.images?.find((i) => i.isCover)?.url ?? experience.images?.[0]?.url ?? '',
        }
      : undefined,
    defaultValues: { title: '', description: '', destinationId: '', price: 0, currency: 'USD' },
  })

  if (isEdit && loadingExperience) return <FullScreenSpinner />

  const onSubmit = async (values: FormOutput) => {
    setSubmitError(null)
    const body = {
      title: values.title,
      description: values.description,
      destinationId: values.destinationId,
      categoryIds: [] as string[],
      price: values.price,
      currency: values.currency,
      durationMinutes: values.durationMinutes === '' ? undefined : Number(values.durationMinutes),
      durationLabel: values.durationLabel || undefined,
      includesText: values.includesText || undefined,
      excludesText: values.excludesText || undefined,
      images: values.coverImageUrl ? [{ url: values.coverImageUrl, isCover: true }] : [],
    }
    try {
      if (isEdit) {
        await updateMutation.mutateAsync({ id, body })
      } else {
        await createMutation.mutateAsync(body)
      }
      navigate('/provider/experiences')
    } catch (error) {
      setSubmitError(getErrorMessage(error))
    }
  }

  return (
    <div className="max-w-2xl">
      <PageHeader title={isEdit ? 'Editar experiencia' : 'Nueva experiencia'} description="UC-P-04/05." />
      <Alert variant="info" className="mb-6">
        Las categorías todavía no están disponibles desde el Backoffice — se crea sin categorías (ver reporte de la sesión).
      </Alert>
      <Card>
        <CardContent className="pt-6">
          <form className="flex flex-col gap-4" onSubmit={form.handleSubmit(onSubmit)} noValidate>
            <div className="flex flex-col gap-1.5">
              <Label>Título</Label>
              <Input {...form.register('title')} />
              {form.formState.errors.title && (
                <p className="text-xs text-destructive">{form.formState.errors.title.message}</p>
              )}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label>Descripción</Label>
              <Textarea rows={4} {...form.register('description')} />
              {form.formState.errors.description && (
                <p className="text-xs text-destructive">{form.formState.errors.description.message}</p>
              )}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label>Destino (ciudad)</Label>
              <Select
                value={form.watch('destinationId')}
                onValueChange={(value) => {
                  // Radix Select puede disparar onValueChange("") una vez justo después de montar,
                  // antes de que sus SelectItem terminen de registrarse — aplicar ese valor vacío
                  // pisaba el destinationId ya cargado en edición (bug encontrado en Oleada 4).
                  // No hay ninguna opción "vacía" real en esta lista, así que ignorar "" es seguro.
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

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div className="flex flex-col gap-1.5">
                <Label>Precio</Label>
                <Input type="number" step="0.01" min="0" {...form.register('price')} />
                {form.formState.errors.price && (
                  <p className="text-xs text-destructive">{form.formState.errors.price.message}</p>
                )}
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

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div className="flex flex-col gap-1.5">
                <Label>Duración (minutos, opcional)</Label>
                <Input type="number" min="1" {...form.register('durationMinutes')} />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label>Etiqueta de duración (opcional)</Label>
                <Input placeholder="ej. Medio día" {...form.register('durationLabel')} />
              </div>
            </div>

            <div className="flex flex-col gap-1.5">
              <Label>Incluye (opcional)</Label>
              <Textarea rows={2} {...form.register('includesText')} />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label>No incluye (opcional)</Label>
              <Textarea rows={2} {...form.register('excludesText')} />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label>URL de imagen de portada (opcional)</Label>
              <Input placeholder="https://…" {...form.register('coverImageUrl')} />
              {form.formState.errors.coverImageUrl && (
                <p className="text-xs text-destructive">{form.formState.errors.coverImageUrl.message}</p>
              )}
            </div>

            {submitError && <Alert variant="destructive">{submitError}</Alert>}
            <Button type="submit" disabled={form.formState.isSubmitting} className="self-start">
              {isEdit ? 'Guardar cambios' : 'Crear experiencia'}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
