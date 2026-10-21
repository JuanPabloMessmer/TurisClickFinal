import {
  addMonths,
  buildMonthGrid,
  compareYearMonth,
  monthLabel,
  parseIsoDate,
  WEEKDAY_SHORT_LABELS,
  yearMonthOf,
  type IsoDate,
  type YearMonth,
} from '@turisclick/utils'
import { useState } from 'react'
import { Pressable, Text, View } from 'react-native'
import {
  maxDate,
  rangeDayState,
  selectDay,
  type DateField,
  type DateRange,
  type RangeDayState,
  type TripType,
} from '@/features/flights/search/model'
import { SheetModal } from '@/features/flights/search/SheetModal'
import { Button } from '@/ui'

const longDate = new Intl.DateTimeFormat('es-BO', { weekday: 'long', day: 'numeric', month: 'long' })
const shortDate = new Intl.DateTimeFormat('es-BO', { weekday: 'short', day: 'numeric', month: 'short' })

export function formatShortDate(iso: IsoDate | null) {
  return iso ? shortDate.format(parseIsoDate(iso)) : 'Elegir'
}

/**
 * Calendario de ida y vuelta. Se toca la ida y el calendario pasa solo a la vuelta, con los días del medio
 * resaltados; los días que no se pueden elegir —pasados, demasiado lejanos o, eligiendo la vuelta,
 * anteriores a la ida— están deshabilitados, así que no hay forma de armar un viaje imposible.
 *
 * Trabaja sobre una copia: nada cambia en el buscador hasta tocar "Listo".
 */
export function DateRangeModal({
  visible,
  tripType,
  value,
  today,
  onConfirm,
  onClose,
}: {
  visible: boolean
  tripType: TripType
  value: DateRange
  today: IsoDate
  onConfirm: (range: DateRange) => void
  onClose: () => void
}) {
  const [range, setRange] = useState<DateRange>(value)
  const [editing, setEditing] = useState<DateField>('departure')
  const [month, setMonth] = useState<YearMonth>(() => yearMonthOf(value.departure ?? today))

  const [lastVisible, setLastVisible] = useState(visible)
  if (visible !== lastVisible) {
    setLastVisible(visible)
    if (visible) {
      setRange(value)
      setEditing('departure')
      setMonth(yearMonthOf(value.departure ?? today))
    }
  }

  const first = yearMonthOf(today)
  const last = yearMonthOf(maxDate(today))
  const canPrev = compareYearMonth(month, first) > 0
  const canNext = compareYearMonth(month, last) < 0

  const roundTrip = tripType === 'ROUND_TRIP'
  const complete = Boolean(range.departure) && (!roundTrip || Boolean(range.return))

  const pick = (iso: IsoDate) => {
    const next = selectDay(range, editing, iso, tripType)
    setRange(next.range)
    setEditing(next.editing)
  }

  return (
    <SheetModal
      visible={visible}
      title={roundTrip ? 'Fechas de ida y vuelta' : 'Fecha de ida'}
      onClose={onClose}
      footer={
        <Button
          label={complete ? 'Listo' : roundTrip && range.departure ? 'Elegí la vuelta' : 'Elegí la ida'}
          disabled={!complete}
          onPress={() => onConfirm(range)}
        />
      }
    >
      <View className="px-5 pt-4">
        <View className="flex-row gap-3">
          <DateTab
            label="Ida"
            value={range.departure}
            active={editing === 'departure'}
            onPress={() => setEditing('departure')}
          />
          {roundTrip ? (
            <DateTab
              label="Vuelta"
              value={range.return}
              active={editing === 'return'}
              disabled={!range.departure}
              onPress={() => setEditing('return')}
            />
          ) : null}
        </View>

        <View className="mt-5 flex-row items-center justify-between">
          <MonthArrow label="‹" accessibilityLabel="Mes anterior" disabled={!canPrev} onPress={() => setMonth(addMonths(month, -1))} />
          <Text accessibilityRole="header" className="font-ui700 text-heading capitalize text-ink">
            {monthLabel(month)}
          </Text>
          <MonthArrow label="›" accessibilityLabel="Mes siguiente" disabled={!canNext} onPress={() => setMonth(addMonths(month, 1))} />
        </View>

        <View className="mb-1 mt-3 flex-row">
          {WEEKDAY_SHORT_LABELS.map((label) => (
            <Text key={label} className="flex-1 text-center font-ui600 text-caption text-ink-muted">
              {label}
            </Text>
          ))}
        </View>

        {buildMonthGrid(month).map((week) => (
          <View key={week[0].iso} className="flex-row">
            {week.map((cell) =>
              cell.inMonth ? (
                <DayCell
                  key={cell.iso}
                  iso={cell.iso}
                  day={cell.day}
                  state={rangeDayState(cell.iso, range, editing, today)}
                  onPress={() => pick(cell.iso)}
                />
              ) : (
                <View key={cell.iso} className="h-12 flex-1" />
              ),
            )}
          </View>
        ))}

        <Text className="mt-4 text-center font-sans text-caption text-ink-muted">
          {editing === 'return' ? 'Elegí el día de la vuelta.' : 'Elegí el día de la ida.'}
        </Text>
      </View>
    </SheetModal>
  )
}

function DateTab({
  label,
  value,
  active,
  disabled = false,
  onPress,
}: {
  label: string
  value: IsoDate | null
  active: boolean
  disabled?: boolean
  onPress: () => void
}) {
  return (
    <Pressable
      accessibilityRole="tab"
      accessibilityLabel={`${label}: ${value ? longDate.format(parseIsoDate(value)) : 'sin elegir'}`}
      accessibilityState={{ selected: active, disabled }}
      disabled={disabled}
      onPress={onPress}
      className={`flex-1 rounded-md border px-3 py-2.5 ${active ? 'border-primary bg-primary/5' : 'border-border bg-surface'} ${
        disabled ? 'opacity-40' : 'active:opacity-70'
      }`}
    >
      <Text className="font-ui600 text-caption uppercase tracking-wide text-ink-muted">{label}</Text>
      <Text className="mt-0.5 font-ui600 text-body text-ink">{formatShortDate(value)}</Text>
    </Pressable>
  )
}

function DayCell({ iso, day, state, onPress }: { iso: IsoDate; day: number; state: RangeDayState; onPress: () => void }) {
  const disabled = state === 'disabled'
  const edge = state === 'start' || state === 'end' || state === 'start-end'
  const band =
    state === 'in-range'
      ? 'bg-primary/10'
      : state === 'start'
        ? 'bg-primary/10 rounded-l-full'
        : state === 'end'
          ? 'bg-primary/10 rounded-r-full'
          : ''

  const detail =
    state === 'start' ? ', ida' : state === 'end' ? ', vuelta' : state === 'start-end' ? ', ida y vuelta' : disabled ? ', no disponible' : ''

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${longDate.format(parseIsoDate(iso))}${detail}`}
      accessibilityState={{ disabled, selected: edge }}
      disabled={disabled}
      onPress={onPress}
      className={`h-12 flex-1 items-center justify-center ${band}`}
    >
      <View className={`h-10 w-10 items-center justify-center rounded-full ${edge ? 'bg-primary' : ''}`}>
        <Text
          className={`text-base ${
            edge ? 'font-ui700 text-white' : disabled ? 'text-[#CBD5E1]' : state === 'in-range' ? 'font-ui600 text-primary' : 'text-ink'
          }`}
        >
          {day}
        </Text>
      </View>
    </Pressable>
  )
}

function MonthArrow({
  label,
  accessibilityLabel,
  disabled,
  onPress,
}: {
  label: string
  accessibilityLabel: string
  disabled: boolean
  onPress: () => void
}) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      className={`h-11 w-11 items-center justify-center rounded-full ${disabled ? 'opacity-25' : 'bg-surface active:opacity-70'}`}
    >
      <Text className="text-2xl text-primary">{label}</Text>
    </Pressable>
  )
}
