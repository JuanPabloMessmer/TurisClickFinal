import type {
  BudgetLevelValue,
  TouristPreferencesResponse,
  TravelPaceValue,
  TravelPartyValue,
  UpdateTouristPreferencesRequest,
} from '@turisclick/api-client'

/**
 * Onboarding de preferencias: solo se pregunta lo que el asistente usa (intereses, ritmo, con quién,
 * gasto). Todo es opcional; el backend persiste y el asistente lo toma como punto de partida.
 */

export interface PreferenceOption<T extends string> {
  value: T
  emoji: string
  title: string
  description: string
}

export const PACE_OPTIONS: PreferenceOption<TravelPaceValue>[] = [
  { value: 'RELAXED', emoji: '🌿', title: 'Tranquilo', description: 'Una actividad por día y tiempo libre para disfrutar.' },
  { value: 'BALANCED', emoji: '⚖️', title: 'Equilibrado', description: 'Actividades con pausas, sin correr.' },
  { value: 'INTENSE', emoji: '⚡', title: 'Intenso', description: 'Aprovechar el día al máximo, varias actividades.' },
]

export const PARTY_OPTIONS: PreferenceOption<TravelPartyValue>[] = [
  { value: 'SOLO', emoji: '🎒', title: 'Solo/a', description: 'Planes a tu medida.' },
  { value: 'COUPLE', emoji: '💑', title: 'En pareja', description: 'Experiencias para dos.' },
  { value: 'FRIENDS', emoji: '🙌', title: 'Con amigos', description: 'Grupos y buena energía.' },
  { value: 'FAMILY', emoji: '👨‍👩‍👧', title: 'En familia', description: 'Planes para todas las edades.' },
]

/** Los montos son los mismos que usa el backend para filtrar (TouristPreferenceHints): no se inventan rangos. */
export const BUDGET_OPTIONS: PreferenceOption<BudgetLevelValue>[] = [
  { value: 'ECONOMY', emoji: '🪙', title: 'Económico', description: 'Hasta Bs 250 por actividad.' },
  { value: 'MODERATE', emoji: '💵', title: 'Moderado', description: 'Hasta Bs 600 por actividad.' },
  { value: 'PREMIUM', emoji: '💎', title: 'Sin límite', description: 'Lo mejor, aunque cueste más.' },
]

const CATEGORY_EMOJI: Record<string, string> = {
  naturaleza: '🏞️',
  aventura: '🧗',
  historia: '🏛️',
  cultura: '🎭',
  gastronomia: '🍲',
  'relax y bienestar': '🧘',
}

const fold = (text: string) => text.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')

export function categoryEmoji(name?: string | null) {
  return CATEGORY_EMOJI[fold(name ?? '')] ?? '✨'
}

export const MAX_INTERESTS = 12

export interface PreferencesDraft {
  categoryIds: string[]
  travelPace: TravelPaceValue | null
  travelParty: TravelPartyValue | null
  budgetLevel: BudgetLevelValue | null
}

export const EMPTY_DRAFT: PreferencesDraft = { categoryIds: [], travelPace: null, travelParty: null, budgetLevel: null }

const isOneOf = <T extends string>(options: PreferenceOption<T>[], value?: string | null): value is T =>
  options.some((option) => option.value === value)

/** Borrador a partir de lo guardado (edición desde Perfil). Valores desconocidos se descartan. */
export function draftFrom(preferences?: TouristPreferencesResponse | null): PreferencesDraft {
  if (!preferences) return EMPTY_DRAFT
  return {
    categoryIds: (preferences.categories ?? []).flatMap((c) => (c.id ? [c.id] : [])),
    travelPace: isOneOf(PACE_OPTIONS, preferences.travelPace) ? preferences.travelPace : null,
    travelParty: isOneOf(PARTY_OPTIONS, preferences.travelParty) ? preferences.travelParty : null,
    budgetLevel: isOneOf(BUDGET_OPTIONS, preferences.budgetLevel) ? preferences.budgetLevel : null,
  }
}

export function toggleInterest(draft: PreferencesDraft, categoryId: string): PreferencesDraft {
  if (draft.categoryIds.includes(categoryId)) {
    return { ...draft, categoryIds: draft.categoryIds.filter((id) => id !== categoryId) }
  }
  if (draft.categoryIds.length >= MAX_INTERESTS) return draft
  return { ...draft, categoryIds: [...draft.categoryIds, categoryId] }
}

/** Tocar la opción ya elegida la deselecciona: todas las preguntas son opcionales. */
export function toggleSingle<K extends 'travelPace' | 'travelParty' | 'budgetLevel'>(
  draft: PreferencesDraft,
  key: K,
  value: NonNullable<PreferencesDraft[K]>,
): PreferencesDraft {
  return { ...draft, [key]: draft[key] === value ? null : value }
}

export function toRequest(draft: PreferencesDraft, completeOnboarding: boolean): UpdateTouristPreferencesRequest {
  return {
    categoryIds: draft.categoryIds,
    travelPace: draft.travelPace,
    travelParty: draft.travelParty,
    budgetLevel: draft.budgetLevel,
    completeOnboarding,
  }
}

export const ONBOARDING_STEPS = ['interests', 'pace', 'party', 'budget', 'summary'] as const
export type OnboardingStep = (typeof ONBOARDING_STEPS)[number]

const titleOf = <T extends string>(options: PreferenceOption<T>[], value?: string | null) =>
  options.find((option) => option.value === value)?.title

/** Resumen legible del perfil, para Perfil y para el paso final. */
export function preferenceSummary(preferences: TouristPreferencesResponse | null | undefined) {
  return {
    interests: (preferences?.categories ?? []).map((c) => c.name ?? '').filter(Boolean),
    pace: titleOf(PACE_OPTIONS, preferences?.travelPace) ?? null,
    party: titleOf(PARTY_OPTIONS, preferences?.travelParty) ?? null,
    budget: titleOf(BUDGET_OPTIONS, preferences?.budgetLevel) ?? null,
  }
}

export function isEmptyProfile(preferences: TouristPreferencesResponse | null | undefined) {
  const summary = preferenceSummary(preferences)
  return summary.interests.length === 0 && !summary.pace && !summary.party && !summary.budget
}
