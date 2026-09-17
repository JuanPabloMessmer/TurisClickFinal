import { formatDate } from '@turisclick/utils'
import { useRouter } from 'expo-router'
import { useState } from 'react'
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
import { FormError, Price, Screen, SectionHeader, Skeleton } from '@/ui'

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
      <View className="mx-5 mt-2 rounded-3xl bg-primary p-5" style={{ elevation: 4 }}>
        <View className="flex-row items-center gap-3">
          <AssistantAvatar size={40} />
          <View className="flex-1">
            <Text className="text-sm font-medium text-white/80">Asistente TurisClick</Text>
            <Text className="text-xl font-bold text-white">Hola{user?.firstName ? `, ${user.firstName}` : ''}. ¿A dónde vamos?</Text>
          </View>
        </View>
        <Text className="mt-3 text-sm leading-5 text-white/85">
          Contame destino, días y qué te gusta. Armo el viaje con actividades reales que podés reservar.
        </Text>
        <View className="mt-4 flex-row items-end gap-2 rounded-2xl bg-surface p-2">
          <TextInput
            accessibilityLabel="Contale al asistente qué viaje querés"
            placeholder="Ej: 3 días en Sucre, algo cultural"
            placeholderTextColor={colors.inkMuted}
            value={draft}
            onChangeText={setDraft}
            multiline
            maxLength={2000}
            className="max-h-28 min-h-[44px] flex-1 px-2 py-2 text-base text-ink"
          />
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Enviar"
            disabled={!draft.trim() || start.isPending}
            onPress={() => send(draft)}
            className={`h-11 w-11 items-center justify-center rounded-xl bg-accent ${!draft.trim() || start.isPending ? 'opacity-40' : 'active:opacity-70'}`}
          >
            <Text className="text-lg font-bold text-ink">{start.isPending ? '…' : '➤'}</Text>
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
              className="flex-row items-center gap-3 rounded-2xl bg-surface p-4 active:opacity-80"
              style={{ elevation: 1 }}
            >
              <Text className="text-lg">💬</Text>
              <Text className="flex-1 text-base text-ink">{prompt}</Text>
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
            <Text className="text-sm text-[#5B7285]">Todavía no guardaste ninguno.</Text>
          ) : (
            (saved.data?.items ?? []).map((itinerary) => {
              const status = itineraryStatusLabel(itinerary.status)
              return (
                <Pressable
                  key={itinerary.id}
                  accessibilityRole="button"
                  onPress={() => router.push({ pathname: '/assistant/itinerary/[id]', params: { id: itinerary.id ?? '' } })}
                  className="rounded-2xl bg-surface p-4 active:opacity-80"
                  style={{ elevation: 1 }}
                >
                  <View className="flex-row items-center justify-between">
                    <Text className="flex-1 pr-2 text-base font-semibold text-ink" numberOfLines={1}>
                      🗺️ {itinerary.title ?? 'Itinerario'}
                    </Text>
                    <ToneBadge label={status.label} tone={status.tone} />
                  </View>
                  <View className="mt-2 flex-row items-center justify-between">
                    <Text className="text-sm text-[#5B7285]">
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
            <Text className="text-sm text-[#5B7285]">Tus conversaciones con el asistente van a aparecer acá.</Text>
          ) : (
            (conversations.data?.items ?? []).map((conversation) => (
              <Pressable
                key={conversation.id}
                accessibilityRole="button"
                onPress={() => router.push({ pathname: '/assistant/[id]', params: { id: conversation.id ?? '' } })}
                className="flex-row items-center justify-between rounded-2xl bg-surface p-4 active:opacity-80"
                style={{ elevation: 1 }}
              >
                <View className="flex-1 pr-3">
                  <Text className="text-base font-semibold text-ink">
                    {conversation.preferredDestinationName ? `Viaje a ${conversation.preferredDestinationName}` : 'Conversación nueva'}
                  </Text>
                  <Text className="text-sm text-[#5B7285]">
                    {conversation.updatedAt ? `Actualizada ${formatDate(conversation.updatedAt)}` : ''}
                  </Text>
                </View>
                <Text className="text-lg text-primary">›</Text>
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
      <Pressable accessibilityRole="button" onPress={goEdit} className="mx-5 mt-4 flex-row items-center gap-3 rounded-2xl border border-dashed border-primary/50 bg-surface p-4 active:opacity-80">
        <Text className="text-2xl">🧭</Text>
        <Text className="flex-1 text-sm text-ink">Completá tu perfil de viaje y no vas a tener que repetir tus gustos en cada conversación.</Text>
      </Pressable>
    )
  }

  const parts = [summary.interests.join(', '), summary.pace && `ritmo ${summary.pace.toLowerCase()}`, summary.party, summary.budget && `presupuesto ${summary.budget.toLowerCase()}`].filter(Boolean)
  return (
    <View className="mx-5 mt-4 flex-row items-center gap-3 rounded-2xl bg-secondary/10 p-4">
      <Text className="text-2xl">🧭</Text>
      <View className="flex-1">
        <Text className="text-xs font-semibold uppercase tracking-wide text-secondary">Parto de tu perfil</Text>
        <Text className="mt-0.5 text-sm text-ink">{parts.join(' · ')}</Text>
      </View>
      <Pressable accessibilityRole="button" onPress={goEdit} className="px-1 py-1 active:opacity-60">
        <Text className="text-sm font-semibold text-primary">Ajustar</Text>
      </Pressable>
    </View>
  )
}
