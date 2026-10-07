import type { CancellationLineResponse, CancellationQuoteResponse, MoneyLineResponse } from '@turisclick/api-client'
import { formatCurrency } from '@turisclick/utils'
import { TriangleAlert } from 'lucide-react-native'
import { ScrollView, Text, View } from 'react-native'
import { colors } from '@/theme/colors'
import { Button, Icon, Skeleton, StateBadge } from '@/ui'

/**
 * Lo que la persona ve ANTES de cancelar: qué se cancela, cuánto vuelve de cada parte y por qué.
 *
 * Todos los números vienen del backend. La pantalla no calcula un reembolso ni suma nada que el servidor no
 * haya sumado: si el paquete y el pasaje están en monedas distintas, se muestran dos importes, porque no
 * existe un tipo de cambio en este sistema.
 *
 * El detalle por componente no es un lujo informativo: la política del operador y la de la aerolínea son
 * cosas distintas, y mostrar un solo total escondería que el pasaje puede no devolver nada aunque el paquete
 * sí.
 */
export function CancellationPreview({
  quote,
  loading,
  error,
  submitting,
  onConfirm,
  onBack,
}: {
  quote?: CancellationQuoteResponse
  loading: boolean
  error?: string | null
  submitting: boolean
  onConfirm: () => void
  onBack: () => void
}) {
  if (loading) {
    return (
      <View className="gap-3" accessibilityLabel="Calculando el reembolso">
        <Skeleton className="h-20 w-full" />
        <Skeleton className="h-28 w-full" />
        <Skeleton className="h-12 w-full" />
      </View>
    )
  }

  if (error || !quote) {
    return (
      <View className="gap-4">
        <View accessibilityRole="alert" className="rounded-2xl bg-danger-bg p-4">
          <Text className="text-sm leading-5 text-danger-fg">
            {error ?? 'No pudimos calcular el reembolso de esta reserva.'}
          </Text>
        </View>
        <Button label="Volver" variant="outline" onPress={onBack} />
      </View>
    )
  }

  const lines = quote.lines ?? []

  return (
    <ScrollView contentContainerStyle={{ gap: 16, paddingBottom: 32 }} showsVerticalScrollIndicator={false}>
      <View>
        <Text className="text-lg font-bold text-ink">Esto es lo que pasa si cancelás</Text>
        <Text className="mt-1 text-sm leading-5 text-[#5B7285]">{quote.summary}</Text>
      </View>

      <View className="gap-3">
        {lines.map((line, index) => (
          <CancellationLineCard key={index} line={line} />
        ))}
      </View>

      <TotalsBlock label="Te devolvemos" totals={quote.refunds} emptyLabel="No hay reembolso" />
      <TotalsBlock label="Se retiene" totals={quote.fees} />

      {quote.hasUnknownRefund ? (
        <View className="rounded-2xl bg-warning-bg p-4">
          <Text className="text-sm leading-5 text-warning-fg">
            La aerolínea todavía no informó cuánto devuelve por el pasaje. Lo vas a ver en el detalle de la
            reserva en cuanto lo confirme.
          </Text>
        </View>
      ) : null}

      <View accessibilityRole="alert" className="flex-row items-start gap-2 rounded-2xl bg-[#E8EEF2] p-4">
        <Icon icon={TriangleAlert} size={16} color={colors.ink} />
        <Text className="flex-1 text-sm leading-5 text-ink">
          Cancelar no se puede deshacer. Si confirmás, se libera tu lugar y —si corresponde— se cancela el
          pasaje con la aerolínea.
        </Text>
      </View>

      <View className="gap-3">
        <Button label="Sí, cancelar la reserva" loading={submitting} onPress={onConfirm} />
        <Button label="No cancelar" variant="outline" disabled={submitting} onPress={onBack} />
      </View>
    </ScrollView>
  )
}

const COMPONENT_LABELS: Record<string, string> = {
  PACKAGE: 'Paquete',
  EXPERIENCE: 'Experiencia',
  FLIGHT: 'Vuelo',
}

function CancellationLineCard({ line }: { line: CancellationLineResponse }) {
  const currency = line.currency ?? 'USD'
  const paid = line.paidAmount ?? 0
  const refund = line.refundAmount ?? 0
  const fee = line.feeAmount ?? 0
  const known = line.refundKnown !== false

  return (
    <View className="rounded-2xl bg-surface p-4" style={{ elevation: 1 }}>
      <View className="flex-row items-center justify-between gap-2">
        <Text className="text-xs font-medium uppercase tracking-wide text-secondary">
          {COMPONENT_LABELS[line.component ?? ''] ?? 'Servicio'}
        </Text>
        {!known ? <StateBadge label="Sin confirmar" tone="warning" /> : null}
      </View>

      <Text className="mt-1 text-base font-semibold text-ink">{line.label}</Text>

      <View className="mt-3 gap-1">
        <Row label="Pagaste" value={formatCurrency(paid, currency)} />
        <Row
          label="Reembolso"
          value={known ? formatCurrency(refund, currency) : 'A confirmar'}
          emphasis={known && refund > 0}
        />
        {known && fee > 0 ? <Row label="Se retiene" value={formatCurrency(fee, currency)} /> : null}
      </View>

      <Text className="mt-2 text-xs leading-5 text-[#5B7285]">{line.explanation}</Text>
    </View>
  )
}

function Row({ label, value, emphasis = false }: { label: string; value: string; emphasis?: boolean }) {
  return (
    <View className="flex-row items-baseline justify-between">
      <Text className="text-sm text-[#5B7285]">{label}</Text>
      <Text className={`text-sm ${emphasis ? 'font-bold text-ink' : 'text-ink'}`}>{value}</Text>
    </View>
  )
}

/** Totales por moneda. Nunca uno solo si las monedas difieren: no hay conversión. */
function TotalsBlock({
  label,
  totals,
  emptyLabel,
}: {
  label: string
  totals?: MoneyLineResponse[] | null
  emptyLabel?: string
}) {
  const rows = (totals ?? []).filter((total) => (total.amount ?? 0) > 0)

  if (rows.length === 0) {
    if (!emptyLabel) return null
    return (
      <View className="rounded-2xl bg-surface p-4" style={{ elevation: 1 }}>
        <Text className="text-sm text-[#5B7285]">{label}</Text>
        <Text className="mt-1 text-base font-semibold text-ink">{emptyLabel}</Text>
      </View>
    )
  }

  return (
    <View className="rounded-2xl bg-surface p-4" style={{ elevation: 1 }} accessibilityLabel={label}>
      <Text className="text-sm text-[#5B7285]">{label}</Text>
      <View className="mt-1 gap-0.5">
        {rows.map((total) => (
          <Text key={total.currency} className="text-lg font-bold text-ink">
            {formatCurrency(total.amount ?? 0, total.currency ?? 'USD')}
          </Text>
        ))}
      </View>
      {rows.length > 1 ? (
        <Text className="mt-2 text-xs leading-5 text-[#5B7285]">
          Son dos monedas distintas, así que se devuelven por separado.
        </Text>
      ) : null}
    </View>
  )
}
