import { useEffect, useState } from 'react'
import { Pressable, ScrollView, Text, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useCategories } from '@/features/catalog/queries'
import { useMyPreferences, useSavePreferences } from '@/features/preferences/api'
import {
  BUDGET_OPTIONS,
  categoryEmoji,
  draftFrom,
  EMPTY_DRAFT,
  ONBOARDING_STEPS,
  PACE_OPTIONS,
  PARTY_OPTIONS,
  toggleInterest,
  toggleSingle,
  toRequest,
  type PreferenceOption,
  type PreferencesDraft,
} from '@/features/preferences/model'
import { toApiError } from '@/lib/errors'
import { Button, ErrorState, FormError, Screen, Skeleton } from '@/ui'

const STEP_COPY = {
  interests: { title: '¿Qué te gusta hacer cuando viajás?', subtitle: 'Elegí todo lo que te atraiga. Lo usamos para recomendarte.' },
  pace: { title: '¿Cómo te gusta viajar?', subtitle: 'Así sabemos cuántas actividades proponerte por día.' },
  party: { title: '¿Con quién viajás normalmente?', subtitle: 'Solo o en pareja, ya no te preguntamos cuántos viajan.' },
  budget: { title: '¿Cuánto preferís gastar?', subtitle: 'Por actividad, en bolivianos. Podés cambiarlo cuando quieras.' },
  summary: { title: 'Tu perfil de viaje', subtitle: 'El asistente lo usa como punto de partida. Lo que le pidas en cada viaje siempre tiene prioridad.' },
} as const

/**
 * Onboarding corto (4 preguntas + confirmación). En modo "onboarding" se puede saltear; en modo "edit"
 * (desde Perfil) arranca con lo guardado. Nada se persiste hasta confirmar o saltear: salir a la mitad no
 * deja un perfil a medias.
 */
export function OnboardingFlow({
  mode,
  firstName,
  onDone,
}: {
  mode: 'onboarding' | 'edit'
  firstName?: string | null
  onDone: () => void
}) {
  const insets = useSafeAreaInsets()
  const categories = useCategories()
  const saved = useMyPreferences()
  const save = useSavePreferences()
  const [stepIndex, setStepIndex] = useState(0)
  const [draft, setDraft] = useState<PreferencesDraft>(EMPTY_DRAFT)
  const [hydrated, setHydrated] = useState(mode === 'onboarding')

  // En edición, el borrador arranca con lo guardado apenas llega (una sola vez).
  useEffect(() => {
    if (!hydrated && saved.data) {
      setDraft(draftFrom(saved.data))
      setHydrated(true)
    }
  }, [hydrated, saved.data])

  const step = ONBOARDING_STEPS[stepIndex]
  const isLast = stepIndex === ONBOARDING_STEPS.length - 1
  const copy = STEP_COPY[step]

  const submit = (complete: boolean) => {
    save.mutate(toRequest(draft, complete), { onSuccess: onDone })
  }

  if (mode === 'edit' && !hydrated) {
    return (
      <Screen edges={['top', 'bottom']}>
        <View className="gap-3 p-5">
          <Skeleton className="h-8 w-3/4" />
          <Skeleton className="h-40 w-full" />
        </View>
      </Screen>
    )
  }

  return (
    <Screen edges={['top']}>
      <View className="flex-row items-center justify-between px-5 pt-2">
        <Text className="text-sm font-semibold text-[#5B7285]">
          {step === 'summary' ? 'Último paso' : `Paso ${stepIndex + 1} de ${ONBOARDING_STEPS.length - 1}`}
        </Text>
        {mode === 'onboarding' && !isLast ? (
          <Pressable accessibilityRole="button" onPress={() => submit(true)} disabled={save.isPending} className="px-2 py-2 active:opacity-60">
            <Text className="text-sm font-semibold text-primary">Saltar</Text>
          </Pressable>
        ) : mode === 'edit' ? (
          <Pressable accessibilityRole="button" onPress={onDone} className="px-2 py-2 active:opacity-60">
            <Text className="text-sm font-semibold text-primary">Cancelar</Text>
          </Pressable>
        ) : (
          <View className="h-9" />
        )}
      </View>

      <View className="mx-5 mt-2 flex-row gap-1.5" accessibilityRole="progressbar" accessibilityValue={{ min: 1, max: ONBOARDING_STEPS.length, now: stepIndex + 1 }}>
        {ONBOARDING_STEPS.map((key, index) => (
          <View key={key} className={`h-1.5 flex-1 rounded-full ${index <= stepIndex ? 'bg-primary' : 'bg-[#E2E8F0]'}`} />
        ))}
      </View>

      <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 160 }} showsVerticalScrollIndicator={false}>
        {stepIndex === 0 && mode === 'onboarding' && firstName ? (
          <Text className="mb-1 text-base text-secondary">¡Bienvenido/a, {firstName}!</Text>
        ) : null}
        <Text accessibilityRole="header" className="text-2xl font-bold text-ink">
          {copy.title}
        </Text>
        <Text className="mb-6 mt-2 text-base leading-6 text-[#5B7285]">{copy.subtitle}</Text>

        {step === 'interests' ? (
          categories.isPending ? (
            <View className="flex-row flex-wrap gap-3">
              {[0, 1, 2, 3].map((k) => (
                <Skeleton key={k} className="h-24 w-[47%]" />
              ))}
            </View>
          ) : categories.isError ? (
            <ErrorState message={toApiError(categories.error).message} onRetry={categories.refetch} />
          ) : (
            <View className="flex-row flex-wrap justify-between gap-y-3">
              {(categories.data ?? []).map((category) => {
                const selected = Boolean(category.id && draft.categoryIds.includes(category.id))
                return (
                  <Pressable
                    key={category.id}
                    accessibilityRole="checkbox"
                    accessibilityState={{ checked: selected }}
                    accessibilityLabel={category.name ?? ''}
                    onPress={() => category.id && setDraft(toggleInterest(draft, category.id))}
                    className={`h-28 w-[48%] justify-between rounded-2xl border-2 p-3.5 active:opacity-80 ${
                      selected ? 'border-primary bg-primary/10' : 'border-[#E2E8F0] bg-surface'
                    }`}
                  >
                    <View className="flex-row items-start justify-between">
                      <Text className="text-3xl">{categoryEmoji(category.name)}</Text>
                      {selected ? (
                        <View className="h-6 w-6 items-center justify-center rounded-full bg-primary">
                          <Text className="text-xs font-bold text-white">✓</Text>
                        </View>
                      ) : null}
                    </View>
                    <Text className={`text-base font-semibold ${selected ? 'text-primary' : 'text-ink'}`}>{category.name}</Text>
                  </Pressable>
                )
              })}
            </View>
          )
        ) : null}

        {step === 'pace' ? (
          <OptionList options={PACE_OPTIONS} value={draft.travelPace} onSelect={(v) => setDraft(toggleSingle(draft, 'travelPace', v))} />
        ) : null}
        {step === 'party' ? (
          <OptionList options={PARTY_OPTIONS} value={draft.travelParty} onSelect={(v) => setDraft(toggleSingle(draft, 'travelParty', v))} />
        ) : null}
        {step === 'budget' ? (
          <OptionList options={BUDGET_OPTIONS} value={draft.budgetLevel} onSelect={(v) => setDraft(toggleSingle(draft, 'budgetLevel', v))} />
        ) : null}

        {step === 'summary' ? (
          <Summary draft={draft} categoryNames={Object.fromEntries((categories.data ?? []).map((c) => [c.id ?? '', c.name ?? '']))} onEdit={setStepIndex} />
        ) : null}

        {save.isError ? (
          <View className="mt-6">
            <FormError message={toApiError(save.error).message} />
          </View>
        ) : null}
      </ScrollView>

      <View
        className="absolute inset-x-0 bottom-0 flex-row gap-3 border-t border-[#E2E8F0] bg-surface px-5 pt-4"
        style={{ paddingBottom: insets.bottom + 16 }}
      >
        {stepIndex > 0 ? (
          <View className="flex-1">
            <Button label="Atrás" variant="outline" onPress={() => setStepIndex(stepIndex - 1)} disabled={save.isPending} />
          </View>
        ) : null}
        <View className="flex-[2]">
          {isLast ? (
            <Button label={mode === 'edit' ? 'Guardar cambios' : 'Empezar a explorar'} loading={save.isPending} onPress={() => submit(true)} />
          ) : (
            <Button label="Siguiente" onPress={() => setStepIndex(stepIndex + 1)} />
          )}
        </View>
      </View>
    </Screen>
  )
}

function OptionList<T extends string>({
  options,
  value,
  onSelect,
}: {
  options: PreferenceOption<T>[]
  value: T | null
  onSelect: (value: T) => void
}) {
  return (
    <View className="gap-3" accessibilityRole="radiogroup">
      {options.map((option) => {
        const selected = option.value === value
        return (
          <Pressable
            key={option.value}
            accessibilityRole="radio"
            accessibilityState={{ selected, checked: selected }}
            accessibilityLabel={`${option.title}. ${option.description}`}
            onPress={() => onSelect(option.value)}
            className={`flex-row items-center rounded-2xl border-2 p-4 active:opacity-80 ${
              selected ? 'border-primary bg-primary/10' : 'border-[#E2E8F0] bg-surface'
            }`}
          >
            <Text className="mr-4 text-3xl">{option.emoji}</Text>
            <View className="flex-1">
              <Text className={`text-base font-bold ${selected ? 'text-primary' : 'text-ink'}`}>{option.title}</Text>
              <Text className="mt-0.5 text-sm text-[#5B7285]">{option.description}</Text>
            </View>
            <View className={`h-6 w-6 items-center justify-center rounded-full border-2 ${selected ? 'border-primary bg-primary' : 'border-[#CBD5E1]'}`}>
              {selected ? <Text className="text-xs font-bold text-white">✓</Text> : null}
            </View>
          </Pressable>
        )
      })}
    </View>
  )
}

function Summary({
  draft,
  categoryNames,
  onEdit,
}: {
  draft: PreferencesDraft
  categoryNames: Record<string, string>
  onEdit: (step: number) => void
}) {
  const title = <T extends string>(options: PreferenceOption<T>[], value: T | null) => {
    const option = options.find((o) => o.value === value)
    return option ? `${option.emoji} ${option.title}` : 'Sin preferencia'
  }
  const rows = [
    { step: 0, label: 'Intereses', value: draft.categoryIds.map((id) => categoryNames[id]).filter(Boolean).join(', ') || 'Sin preferencia' },
    { step: 1, label: 'Ritmo', value: title(PACE_OPTIONS, draft.travelPace) },
    { step: 2, label: 'Viajás', value: title(PARTY_OPTIONS, draft.travelParty) },
    { step: 3, label: 'Presupuesto', value: title(BUDGET_OPTIONS, draft.budgetLevel) },
  ]

  return (
    <View className="gap-3">
      {rows.map((row) => (
        <Pressable
          key={row.label}
          accessibilityRole="button"
          accessibilityLabel={`Cambiar ${row.label}: ${row.value}`}
          onPress={() => onEdit(row.step)}
          className="flex-row items-center justify-between rounded-2xl bg-surface p-4 active:opacity-80"
          style={{ elevation: 1 }}
        >
          <View className="flex-1 pr-3">
            <Text className="text-xs font-semibold uppercase tracking-wide text-[#5B7285]">{row.label}</Text>
            <Text className="mt-1 text-base font-semibold text-ink">{row.value}</Text>
          </View>
          <Text className="text-sm font-semibold text-primary">Cambiar</Text>
        </Pressable>
      ))}
    </View>
  )
}
