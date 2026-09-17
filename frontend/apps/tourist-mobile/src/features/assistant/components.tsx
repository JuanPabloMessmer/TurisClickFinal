import type { ItineraryItemResponse, ItineraryResponse, MessageResponse } from '@turisclick/api-client'
import { formatDate } from '@turisclick/utils'
import { useRouter, type Href } from 'expo-router'
import { useState } from 'react'
import { ActivityIndicator, Modal, Pressable, ScrollView, Text, View } from 'react-native'
import { useItemExplanation } from '@/features/assistant/api'
import {
  availabilityBadge,
  groupByDay,
  itineraryItemTitle,
  itineraryStatusLabel,
  type Tone,
} from '@/features/assistant/model'
import { coverImageUrl } from '@/features/catalog/images'
import { useExperience, usePackage } from '@/features/catalog/queries'
import { toApiError } from '@/lib/errors'
import { colors } from '@/theme/colors'
import { CatalogImage, Price, Skeleton } from '@/ui'

const TONES: Record<Tone, { container: string; text: string }> = {
  success: { container: 'bg-[#DCFCE7]', text: 'text-[#166534]' },
  warning: { container: 'bg-[#FEF3C7]', text: 'text-[#92400E]' },
  danger: { container: 'bg-[#FEE2E2]', text: 'text-[#991B1B]' },
  neutral: { container: 'bg-[#E8EEF2]', text: 'text-[#5B7285]' },
}

export function ToneBadge({ label, tone }: { label: string; tone: Tone }) {
  return (
    <View className={`self-start rounded-full px-2.5 py-1 ${TONES[tone].container}`}>
      <Text className={`text-xs font-semibold ${TONES[tone].text}`}>{label}</Text>
    </View>
  )
}

/** Avatar del asistente: marca visual propia de TurisClick, no un logo de terceros. */
export function AssistantAvatar({ size = 32 }: { size?: number }) {
  return (
    <View className="items-center justify-center rounded-full bg-primary" style={{ width: size, height: size }}>
      <Text style={{ fontSize: size * 0.5 }}>✨</Text>
    </View>
  )
}

export function ChatBubble({ message }: { message: MessageResponse }) {
  const fromAssistant = message.sender === 'AI'
  const pending = message.id?.startsWith('pending-')

  if (!fromAssistant) {
    return (
      <View className="mb-3 max-w-[85%] self-end rounded-3xl rounded-br-md bg-primary px-4 py-3" style={{ opacity: pending ? 0.75 : 1 }}>
        <Text className="text-base leading-6 text-white">{message.content}</Text>
      </View>
    )
  }

  return (
    <View className="mb-3 max-w-[92%] flex-row items-end gap-2 self-start">
      <AssistantAvatar size={28} />
      <View className="flex-1 rounded-3xl rounded-bl-md bg-surface px-4 py-3" style={{ elevation: 1 }}>
        <Text className="text-base leading-6 text-ink">{message.content}</Text>
      </View>
    </View>
  )
}

export function TypingIndicator() {
  return (
    <View accessibilityLabel="El asistente está buscando" className="mb-3 flex-row items-center gap-2 self-start">
      <AssistantAvatar size={28} />
      <View className="flex-row items-center gap-2 rounded-3xl rounded-bl-md bg-surface px-4 py-3" style={{ elevation: 1 }}>
        <ActivityIndicator size="small" color={colors.primary} />
        <Text className="text-sm text-[#5B7285]">Buscando en el catálogo real…</Text>
      </View>
    </View>
  )
}

/** Lo que el asistente tomó del perfil de viaje, para que el turista entienda por qué recomienda eso. */
export function ProfileHints({ hints }: { hints?: string[] | null }) {
  if (!hints?.length) return null
  return (
    <View className="mb-3 ml-9 rounded-2xl border border-secondary/30 bg-secondary/10 px-3 py-2.5">
      <Text className="text-xs font-semibold uppercase tracking-wide text-secondary">Usé tu perfil de viaje</Text>
      <Text className="mt-0.5 text-sm text-ink">{hints.join(' · ')}</Text>
    </View>
  )
}

export function QuickReplies({
  replies,
  onPress,
  disabled,
}: {
  replies: { label: string; message: string }[]
  onPress: (message: string) => void
  disabled?: boolean
}) {
  if (replies.length === 0) return null
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: 8, paddingHorizontal: 16 }}>
      {replies.map((reply) => (
        <Pressable
          key={reply.label}
          accessibilityRole="button"
          accessibilityLabel={reply.message}
          disabled={disabled}
          onPress={() => onPress(reply.message)}
          className={`h-9 flex-row items-center rounded-full border border-primary/40 bg-surface px-3.5 ${disabled ? 'opacity-40' : 'active:opacity-70'}`}
        >
          <Text className="text-sm font-medium text-primary">{reply.label}</Text>
        </Pressable>
      ))}
    </ScrollView>
  )
}

export function WarningsList({ warnings }: { warnings?: string[] | null }) {
  const unique = [...new Set(warnings ?? [])]
  if (unique.length === 0) return null
  return (
    <View className="mt-3 gap-1 rounded-2xl bg-[#FEF3C7] p-3">
      {unique.map((warning) => (
        <Text key={warning} className="text-sm leading-5 text-[#92400E]">
          ⚠️ {warning}
        </Text>
      ))}
    </View>
  )
}

/**
 * Propuesta completa: días, productos reales y totales por moneda. `children` recibe las acciones
 * (guardar/reservar) para reutilizarla en el chat y en el detalle de un itinerario guardado.
 */
export function ItineraryView({
  itinerary,
  children,
}: {
  itinerary: ItineraryResponse
  children?: React.ReactNode
}) {
  const [explaining, setExplaining] = useState<ItineraryItemResponse | null>(null)
  const status = itineraryStatusLabel(itinerary.status)
  const days = groupByDay(itinerary.items)

  return (
    <View className="overflow-hidden rounded-3xl bg-surface" style={{ elevation: 3 }}>
      <View className="bg-primary px-5 pb-4 pt-5">
        <Text className="text-xs font-semibold uppercase tracking-wide text-white/80">Itinerario sugerido · v{itinerary.version}</Text>
        <Text accessibilityRole="header" className="mt-1 text-xl font-bold text-white">
          {itinerary.title ?? 'Tu itinerario'}
        </Text>
        <View className="mt-2 flex-row items-center gap-2">
          <ToneBadge label={status.label} tone={status.tone} />
          <Text className="text-sm text-white/85">
            {days.length} {days.length === 1 ? 'día' : 'días'} · {itinerary.items?.length ?? 0} actividades
          </Text>
        </View>
      </View>

      <View className="gap-5 p-4">
        {days.map((day) => (
          <View key={day.dayNumber}>
            <View className="mb-2 flex-row items-center gap-2">
              <View className="h-7 w-7 items-center justify-center rounded-full bg-accent">
                <Text className="text-xs font-bold text-ink">{day.dayNumber}</Text>
              </View>
              <Text className="text-base font-bold text-ink">Día {day.dayNumber}</Text>
            </View>
            <View className="gap-3">
              {day.items.map((item) => (
                <ItineraryItemCard key={item.id} item={item} onExplain={() => setExplaining(item)} />
              ))}
            </View>
          </View>
        ))}

        <WarningsList warnings={itinerary.warnings} />

        {(itinerary.totals ?? []).length > 0 ? (
          <View className="flex-row items-start justify-between rounded-2xl bg-background p-4">
            <View className="flex-1 pr-3">
              <Text className="text-sm font-semibold text-ink">Total estimado</Text>
              <Text className="text-xs text-[#5B7285]">Precios reales de hoy. Se confirman al reservar.</Text>
            </View>
            <View className="items-end">
              {(itinerary.totals ?? []).map((total) => (
                <Price key={total.currency} amount={total.amount} currency={total.currency} size="lg" />
              ))}
            </View>
          </View>
        ) : null}

        {children}
      </View>

      {itinerary.id ? (
        <ExplanationSheet itineraryId={itinerary.id} item={explaining} onClose={() => setExplaining(null)} />
      ) : null}
    </View>
  )
}

/** Un componente del itinerario. La foto y el destino salen del detalle público del producto (cacheado). */
export function ItineraryItemCard({ item, onExplain }: { item: ItineraryItemResponse; onExplain: () => void }) {
  const router = useRouter()
  const isPackage = item.productType === 'PACKAGE'
  const experience = useExperience(isPackage ? '' : item.experienceId ?? '')
  const pkg = usePackage(isPackage ? item.packageId ?? '' : '')
  const product = isPackage ? pkg.data : experience.data
  const image = coverImageUrl(product?.images)
  const badge = availabilityBadge(item)
  const href = (isPackage ? `/package/${item.packageId}` : `/experience/${item.experienceId}`) as Href
  const title = itineraryItemTitle(item)

  return (
    <View className="overflow-hidden rounded-2xl border border-[#E2E8F0] bg-surface">
      <Pressable accessibilityRole="button" accessibilityLabel={`Ver ${title}`} onPress={() => router.push(href)} className="flex-row active:opacity-80">
        <CatalogImage uri={image} className="h-auto min-h-[112px] w-28" />
        <View className="flex-1 p-3">
          <Text className="text-xs font-medium uppercase tracking-wide text-secondary" numberOfLines={1}>
            {isPackage ? 'Paquete' : 'Experiencia'}
            {product?.destinationName ? ` · ${product.destinationName}` : ''}
          </Text>
          <Text className="mt-0.5 text-base font-semibold text-ink" numberOfLines={2}>
            {title}
          </Text>
          {item.date ? <Text className="mt-1 text-sm text-ink">📅 {formatDate(item.date)}</Text> : null}
          <View className="mt-1.5 flex-row flex-wrap items-center gap-x-2 gap-y-1">
            <Price amount={item.currentPrice ?? item.estimatedUnitPrice} currency={item.currentCurrency ?? item.currency} />
            <Text className="text-xs text-[#5B7285]">× {item.travelers}</Text>
          </View>
          {item.priceChanged ? (
            <Text className="text-xs text-[#92400E]">
              Antes {item.currency} {item.estimatedUnitPrice?.toFixed(2)}
            </Text>
          ) : null}
        </View>
      </Pressable>
      <View className="flex-row items-center justify-between border-t border-[#E2E8F0] px-3 py-2">
        <ToneBadge label={badge.label} tone={badge.tone} />
        <Pressable accessibilityRole="button" accessibilityLabel={`¿Por qué ${title}?`} onPress={onExplain} className="px-2 py-1 active:opacity-60">
          <Text className="text-sm font-semibold text-primary">¿Por qué?</Text>
        </Pressable>
      </View>
    </View>
  )
}

/** Explicación de un componente: el texto y los hechos verificados en los que se basa. */
export function ExplanationSheet({
  itineraryId,
  item,
  onClose,
}: {
  itineraryId: string
  item: ItineraryItemResponse | null
  onClose: () => void
}) {
  const explanation = useItemExplanation(itineraryId, item?.id ?? null)

  return (
    <Modal visible={item !== null} animationType="slide" transparent onRequestClose={onClose}>
      <Pressable accessibilityLabel="Cerrar" onPress={onClose} className="flex-1 bg-black/40" />
      <View className="max-h-[75%] rounded-t-3xl bg-surface px-5 pb-10 pt-4">
        <View className="mb-3 h-1.5 w-12 self-center rounded-full bg-[#CBD5E1]" />
        <View className="flex-row items-center gap-3">
          <AssistantAvatar />
          <Text accessibilityRole="header" className="flex-1 text-lg font-bold text-ink" numberOfLines={2}>
            ¿Por qué {item ? itineraryItemTitle(item) : ''}?
          </Text>
        </View>
        <ScrollView className="mt-4">
          {explanation.isPending ? (
            <View className="gap-2">
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-5/6" />
              <Skeleton className="h-4 w-2/3" />
            </View>
          ) : explanation.isError ? (
            <Text className="text-base text-[#991B1B]">{toApiError(explanation.error).message}</Text>
          ) : (
            <>
              <Text className="text-base leading-6 text-ink">{explanation.data?.explanation}</Text>
              {(explanation.data?.facts ?? []).length > 0 ? (
                <View className="mt-4 rounded-2xl bg-background p-4">
                  <Text className="mb-2 text-xs font-semibold uppercase tracking-wide text-[#5B7285]">Datos verificados en TurisClick</Text>
                  {(explanation.data?.facts ?? []).map((fact) => (
                    <Text key={fact} className="mb-1 text-sm leading-5 text-ink">
                      ✓ {fact}
                    </Text>
                  ))}
                </View>
              ) : null}
            </>
          )}
        </ScrollView>
        <Pressable accessibilityRole="button" onPress={onClose} className="mt-4 h-12 items-center justify-center rounded-2xl border border-primary active:opacity-70">
          <Text className="text-base font-semibold text-primary">Entendido</Text>
        </Pressable>
      </View>
    </Modal>
  )
}
