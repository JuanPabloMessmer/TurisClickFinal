import { formatDate } from '@turisclick/utils'
import { useRouter } from 'expo-router'
import { useState } from 'react'
import { ChevronRight, Compass, Map, MessageCircle, Send } from 'lucide-react-native'
import { Pressable, ScrollView, Text, TextInput, View } from 'react-native'
import { RequireTourist } from '@/auth/RequireTourist'
import { useSession } from '@/auth/session'
import { useConversations, useSavedItineraries, useStartConversation } from '@/features/assistant/api'
import { AssistantAvatar, ToneBadge } from '@/features/assistant/components'
import { itineraryStatusLabel, STARTER_PROMPTS } from '@/features/assistant/model'
import { useMyPreferences } from '@/features/preferences/api'
import { isEmptyProfile, preferenceSummary } from '@/features/preferences/model'
import { toApiError } from '@/lib/errors'
import { colors } from '@/theme/colors'
import { elevation } from '@/theme/elevation'
import { FormError, Icon, Price, Screen, SectionHeader, Skeleton } from '@/ui'

/**
 * Asistente de viajes. Arma itinerarios SOLO con experiencias y paquetes publicados en TurisClick, con
 * precios y cupos reales, y parte del perfil de viaje del turista. Es exclusivo de cuentas de turista.
 */
export default function AssistantScreen() {
  return (
    <Screen>
      <RequireTourist
        title="Tu asistente de viaje"
        message="Iniciá sesión y contale a dónde querés ir: arma tu itinerario con experiencias reales, precios y fechas disponibles."
      >
        <AssistantHome />
      </RequireTourist>
    </Screen>
  )
}

function AssistantHome() {
  const router = useRouter()
  const { user } = useSession()
  const [draft, setDraft] = useState('')
  const start = useStartConversation()
  const conversations = useConversations()
  const saved = useSavedItineraries()

  const send = (message: string) => {
    const content = message.trim()
    if (!content || start.isPending) return
    start.mutate(content, {
      onSuccess: ({ conversation }) => {
        setDraft('')
        if (conversation.id) router.push({ pathname: '/assistant/[id]', params: { id: conversation.id } })
      },
    })
  }

  return (
    <ScrollView contentContainerStyle={{ paddingBottom: 40 }} keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false}>
      <View className="mx-4 mt-2 rounded-lg bg-brand-900 p-5" style={elevation.raised}>
        <View className="flex-row items-center gap-3">
          <AssistantAvatar size={40} />
          <View className="flex-1">
            <Text className="font-ui500 text-caption text-white/80">Asistente TurisClick</Text>
            <Text className="font-display text-title text-white">Hola{user?.firstName ? `, ${user.firstName}` : ''}. ¿A dónde vamos?</Text>
          </View>
        </View>
        <Text className="mt-3 font-sans text-label text-white/85">
          Contame destino, días y qué te gusta. Armo el viaje con actividades reales que podés reservar.
        </Text>
        <View className="mt-4 flex-row items-end gap-2 rounded-md bg-surface p-2">
          <TextInput
            accessibilityLabel="Contale al asistente qué viaje querés"
            placeholder="Ej: 3 días en Sucre, algo cultural"
            placeholderTextColor={colors.inkMuted}
            value={draft}
            onChangeText={setDraft}
            multiline
            maxLength={2000}
            className="max-h-28 min-h-[44px] flex-1 px-2 py-2 font-sans text-base text-ink"
          />
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Enviar"
            disabled={!draft.trim() || start.isPending}
            onPress={() => send(draft)}
            className={`h-11 w-11 items-center justify-center rounded-sm bg-accent ${!draft.trim() || start.isPending ? 'opacity-40' : 'active:opacity-70'}`}
          >
            <Icon icon={Send} size={18} color={colors.ink} />
          </Pressable>
        </View>
      </View>

      {start.isError ? (
        <View className="mx-5 mt-3">
          <FormError message={toApiError(start.error).message} />
        </View>
      ) : null}

      <ProfileStrip />

      <View className="mt-6">
        <SectionHeader title="Probá con" />
        <View className="gap-2 px-5">
          {STARTER_PROMPTS.map((prompt) => (
            <Pressable
              key={prompt}
              accessibilityRole="button"
              disabled={start.isPending}
              onPress={() => send(prompt)}
              className="flex-row items-center gap-3 rounded-md border border-border bg-surface p-4 active:opacity-80"
            >
              <Icon icon={MessageCircle} size={18} color={colors.primary} />
              <Text className="flex-1 font-sans text-body text-ink">{prompt}</Text>
            </Pressable>
          ))}
        </View>
      </View>

      <View className="mt-8">
        <SectionHeader title="Itinerarios guardados" subtitle="Se revalidan con precios y cupos de hoy al abrirlos" />
        <View className="gap-2 px-5">
          {saved.isPending ? (
            <Skeleton className="h-20 w-full" />
          ) : (saved.data?.items ?? []).length === 0 ? (
            <Text className="font-sans text-label text-ink-muted">Todavía no guardaste ninguno.</Text>
          ) : (
            (saved.data?.items ?? []).map((itinerary) => {
              const status = itineraryStatusLabel(itinerary.status)
              return (
                <Pressable
                  key={itinerary.id}
                  accessibilityRole="button"
                  onPress={() => router.push({ pathname: '/assistant/itinerary/[id]', params: { id: itinerary.id ?? '' } })}
                  className="rounded-md border border-border bg-surface p-4 active:opacity-80"
                >
                  <View className="flex-row items-center justify-between gap-2">
                    <View className="flex-1 flex-row items-center gap-2">
                      <Icon icon={Map} size={16} color={colors.primary} />
                      <Text className="flex-1 font-ui600 text-body text-ink" numberOfLines={1}>
                        {itinerary.title ?? 'Itinerario'}
                      </Text>
                    </View>
                    <ToneBadge label={status.label} tone={status.tone} />
                  </View>
                  <View className="mt-2 flex-row items-center justify-between">
                    <Text className="font-sans text-label text-ink-muted">
                      {itinerary.itemCount} actividades · {itinerary.updatedAt ? formatDate(itinerary.updatedAt) : ''}
                    </Text>
                    {(itinerary.totals ?? []).slice(0, 1).map((total) => (
                      <Price key={total.currency} amount={total.amount} currency={total.currency} />
                    ))}
                  </View>
                </Pressable>
              )
            })
          )}
        </View>
      </View>

      <View className="mt-8">
        <SectionHeader title="Conversaciones recientes" />
        <View className="gap-2 px-5">
          {conversations.isPending ? (
            <Skeleton className="h-16 w-full" />
          ) : (conversations.data?.items ?? []).length === 0 ? (
            <Text className="font-sans text-label text-ink-muted">Tus conversaciones con el asistente van a aparecer acá.</Text>
          ) : (
            (conversations.data?.items ?? []).map((conversation) => (
              <Pressable
                key={conversation.id}
                accessibilityRole="button"
                onPress={() => router.push({ pathname: '/assistant/[id]', params: { id: conversation.id ?? '' } })}
                accessibilityLabel={`Abrir ${conversation.preferredDestinationName ? `viaje a ${conversation.preferredDestinationName}` : 'conversación nueva'}`}
                className="flex-row items-center justify-between rounded-md border border-border bg-surface p-4 active:opacity-80"
              >
                <View className="flex-1 pr-3">
                  <Text className="font-ui600 text-body text-ink">
                    {conversation.preferredDestinationName ? `Viaje a ${conversation.preferredDestinationName}` : 'Conversación nueva'}
                  </Text>
                  <Text className="font-sans text-label text-ink-muted">
                    {conversation.updatedAt ? `Actualizada ${formatDate(conversation.updatedAt)}` : ''}
                  </Text>
                </View>
                <Icon icon={ChevronRight} size={18} color={colors.inkMuted} />
              </Pressable>
            ))
          )}
        </View>
      </View>
    </ScrollView>
  )
}

/** Qué sabe el asistente del turista antes de empezar. Sin perfil, invita a completarlo. */
function ProfileStrip() {
  const router = useRouter()
  const { data, isPending } = useMyPreferences()
  if (isPending) return null

  const summary = preferenceSummary(data)
  const goEdit = () => router.push({ pathname: '/onboarding', params: { mode: 'edit' } })

  if (isEmptyProfile(data)) {
    return (
      <Pressable accessibilityRole="button" onPress={goEdit} className="mx-4 mt-4 flex-row items-center gap-3 rounded-md border border-dashed border-border-control bg-surface p-4 active:opacity-80">
        <Icon icon={Compass} size={20} color={colors.primary} />
        <Text className="flex-1 font-sans text-label text-ink">Completá tu perfil de viaje y no vas a tener que repetir tus gustos en cada conversación.</Text>
      </Pressable>
    )
  }

  const parts = [summary.interests.join(', '), summary.pace && `ritmo ${summary.pace.toLowerCase()}`, summary.party, summary.budget && `presupuesto ${summary.budget.toLowerCase()}`].filter(Boolean)
  return (
    <View className="mx-4 mt-4 flex-row items-center gap-3 rounded-md bg-primary/10 p-4">
      <Icon icon={Compass} size={20} color={colors.primary} />
      <View className="flex-1">
        <Text className="font-ui600 text-caption text-primary">Parto de tu perfil</Text>
        <Text className="mt-0.5 font-sans text-label text-ink">{parts.join(' · ')}</Text>
      </View>
      <Pressable accessibilityRole="button" accessibilityLabel="Ajustar tu perfil de viaje" onPress={goEdit} className="h-11 justify-center px-2 active:opacity-60">
        <Text className="font-ui600 text-label text-primary">Ajustar</Text>
      </Pressable>
    </View>
  )
}
