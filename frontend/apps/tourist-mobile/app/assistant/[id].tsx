import { Stack, useLocalSearchParams } from 'expo-router'
import { useEffect, useMemo, useRef, useState } from 'react'
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, Text, TextInput, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { RequireTourist } from '@/auth/RequireTourist'
import { useConversation, useLastReply, useLatestItinerary, useSendMessage } from '@/features/assistant/api'
import {
  ChatBubble,
  ItineraryView,
  ProfileHints,
  QuickReplies,
  TypingIndicator,
  WarningsList,
} from '@/features/assistant/components'
import { ItineraryActions } from '@/features/assistant/ItineraryActions'
import { lastAssistantIndex, quickRepliesFor, REFINEMENTS } from '@/features/assistant/model'
import { byPublishedContent } from '@/features/catalog/destinations'
import { useCities } from '@/features/catalog/queries'
import { ScreenHeader } from '@/features/reservations/components'
import { toApiError } from '@/lib/errors'
import { colors } from '@/theme/colors'
import { ErrorState, FormError, Screen, Skeleton } from '@/ui'

/**
 * Conversación con el asistente. El itinerario vigente se muestra debajo de la última respuesta, y cada
 * mensaje nuevo puede generarlo o ajustarlo (una versión nueva; la anterior queda en el historial).
 */
export default function AssistantChatScreen() {
  const { id } = useLocalSearchParams<{ id: string }>()

  return (
    <Screen edges={['top']}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title="Asistente de viaje" fallback="/assistant" />
      <RequireTourist title="Iniciá sesión para seguir la conversación">
        <Chat conversationId={id} />
      </RequireTourist>
    </Screen>
  )
}

function Chat({ conversationId }: { conversationId: string }) {
  const insets = useSafeAreaInsets()
  const conversation = useConversation(conversationId)
  const itinerary = useLatestItinerary(conversationId)
  const lastReply = useLastReply(conversationId)
  const send = useSendMessage(conversationId)
  const cities = useCities()
  const [draft, setDraft] = useState('')
  const scrollRef = useRef<ScrollView>(null)

  const messages = conversation.data?.messages ?? []
  const anchor = lastAssistantIndex(messages)
  const reply = lastReply.data
  const destinationNames = useMemo(() => byPublishedContent(cities.data).map((c) => c.name ?? '').filter(Boolean), [cities.data])
  const quickReplies = reply?.clarificationNeeded
    ? quickRepliesFor(reply.missingInformation ?? [], destinationNames)
    : itinerary.data
      ? REFINEMENTS
      : []

  useEffect(() => {
    const timer = setTimeout(() => scrollRef.current?.scrollToEnd({ animated: true }), 80)
    return () => clearTimeout(timer)
  }, [messages.length, send.isPending, itinerary.data?.id])

  const submit = (message: string) => {
    const content = message.trim()
    if (!content || send.isPending) return
    setDraft('')
    send.mutate(content)
  }

  if (conversation.isPending) {
    return (
      <View className="gap-3 p-5">
        <Skeleton className="h-16 w-3/4" />
        <Skeleton className="h-16 w-2/3 self-end" />
      </View>
    )
  }

  if (conversation.isError) {
    return <ErrorState message={toApiError(conversation.error).message} onRetry={conversation.refetch} />
  }

  return (
    <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} className="flex-1">
      <ScrollView ref={scrollRef} contentContainerStyle={{ padding: 16, paddingBottom: 24 }} keyboardShouldPersistTaps="handled">
        {messages.length === 0 ? (
          <Text className="mt-8 text-center text-base text-[#5B7285]">Contame a dónde querés ir, cuántos días y qué te gusta.</Text>
        ) : null}

        {messages.map((message, index) => (
          <View key={message.id ?? index}>
            <ChatBubble message={message} />
            {index === anchor ? (
              <>
                {reply ? <ProfileHints hints={reply.profileHints} /> : null}
                {reply ? <WarningsList warnings={reply.warnings} /> : null}
                {itinerary.data ? (
                  <View className="mb-4 mt-2">
                    <ItineraryView itinerary={itinerary.data}>
                      <ItineraryActions itinerary={itinerary.data} />
                    </ItineraryView>
                  </View>
                ) : null}
              </>
            ) : null}
          </View>
        ))}

        {send.isPending ? <TypingIndicator /> : null}
        {send.isError ? <FormError message={toApiError(send.error).message} /> : null}
      </ScrollView>

      <View className="border-t border-[#E2E8F0] bg-surface pt-3" style={{ paddingBottom: insets.bottom + 10 }}>
        <QuickReplies replies={quickReplies} onPress={submit} disabled={send.isPending} />
        <View className="mx-4 mt-3 flex-row items-end gap-2 rounded-2xl border border-[#E2E8F0] bg-background p-1.5">
          <TextInput
            accessibilityLabel="Mensaje para el asistente"
            placeholder={itinerary.data ? 'Pedí un cambio: "algo más barato"…' : 'Escribí tu mensaje…'}
            placeholderTextColor={colors.inkMuted}
            value={draft}
            onChangeText={setDraft}
            multiline
            maxLength={2000}
            className="max-h-28 min-h-[40px] flex-1 px-2 py-2 text-base text-ink"
          />
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Enviar mensaje"
            disabled={!draft.trim() || send.isPending}
            onPress={() => submit(draft)}
            className={`h-10 w-10 items-center justify-center rounded-xl bg-primary ${!draft.trim() || send.isPending ? 'opacity-40' : 'active:opacity-70'}`}
          >
            <Text className="text-base font-bold text-white">➤</Text>
          </Pressable>
        </View>
      </View>
    </KeyboardAvoidingView>
  )
}
