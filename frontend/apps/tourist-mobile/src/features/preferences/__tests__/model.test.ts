import {
  categoryEmoji,
  draftFrom,
  EMPTY_DRAFT,
  isEmptyProfile,
  MAX_INTERESTS,
  preferenceSummary,
  toggleInterest,
  toggleSingle,
  toRequest,
} from '@/features/preferences/model'

describe('borrador de preferencias', () => {
  it('intereses: agrega, quita y respeta el máximo', () => {
    let draft = toggleInterest(EMPTY_DRAFT, 'a')
    draft = toggleInterest(draft, 'b')
    expect(draft.categoryIds).toEqual(['a', 'b'])
    expect(toggleInterest(draft, 'a').categoryIds).toEqual(['b'])

    const full = { ...EMPTY_DRAFT, categoryIds: Array.from({ length: MAX_INTERESTS }, (_, i) => `c${i}`) }
    expect(toggleInterest(full, 'extra')).toBe(full)
  })

  it('opciones únicas: tocar la elegida la deselecciona', () => {
    const relaxed = toggleSingle(EMPTY_DRAFT, 'travelPace', 'RELAXED')
    expect(relaxed.travelPace).toBe('RELAXED')
    expect(toggleSingle(relaxed, 'travelPace', 'INTENSE').travelPace).toBe('INTENSE')
    expect(toggleSingle(relaxed, 'travelPace', 'RELAXED').travelPace).toBeNull()
  })

  it('arma el request tal como lo espera el backend', () => {
    const draft = { categoryIds: ['c1'], travelPace: 'BALANCED' as const, travelParty: null, budgetLevel: 'ECONOMY' as const }
    expect(toRequest(draft, true)).toEqual({
      categoryIds: ['c1'],
      travelPace: 'BALANCED',
      travelParty: null,
      budgetLevel: 'ECONOMY',
      completeOnboarding: true,
    })
  })

  it('parte de lo guardado y descarta valores desconocidos', () => {
    expect(
      draftFrom({ categories: [{ id: 'c1', name: 'Cultura' }, { name: 'sin id' }], travelPace: 'SLOW', travelParty: 'FAMILY', budgetLevel: 'PREMIUM' }),
    ).toEqual({ categoryIds: ['c1'], travelPace: null, travelParty: 'FAMILY', budgetLevel: 'PREMIUM' })
    expect(draftFrom(undefined)).toEqual(EMPTY_DRAFT)
  })
})

describe('resumen', () => {
  it('traduce los valores a textos y detecta el perfil vacío', () => {
    const prefs = { categories: [{ id: '1', name: 'Aventura' }], travelPace: 'INTENSE', travelParty: 'SOLO', budgetLevel: null }
    expect(preferenceSummary(prefs)).toEqual({ interests: ['Aventura'], pace: 'Intenso', party: 'Solo/a', budget: null })
    expect(isEmptyProfile(prefs)).toBe(false)
    expect(isEmptyProfile({ categories: [], onboardingCompleted: true })).toBe(true)
  })

  it('emoji por categoría sin depender de tildes, con uno genérico de reserva', () => {
    expect(categoryEmoji('Gastronomía')).toBe('🍲')
    expect(categoryEmoji('gastronomia')).toBe('🍲')
    expect(categoryEmoji('Buceo')).toBe('✨')
  })
})
