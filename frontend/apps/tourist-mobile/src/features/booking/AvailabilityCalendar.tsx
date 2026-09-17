import { addMonths, monthLabel, parseIsoDate, todayIso, WEEKDAY_SHORT_LABELS, yearMonthOf, type IsoDate, type YearMonth } from '@turisclick/utils'
import { useEffect, useMemo, useState } from 'react'
import { Pressable, Text, View } from 'react-native'
import {
  availableDaysInMonth,
  buildCalendarMonth,
  canGo,
  firstAvailableDate,
  navigationBounds,
  slotsByDate,
  type CalendarDay,
} from '@/features/booking/calendarModel'
import type { BookableSlot } from '@/features/booking/selection'

const longDate = new Intl.DateTimeFormat('es-BO', { weekday: 'long', day: 'numeric', month: 'long' })

/**
 * Calendario mensual para elegir fecha. Reemplaza la lista de fechas: con meses de disponibilidad una
 * lista no escala. Distingue cuatro estados — disponible, seleccionado, sin disponibilidad y pasado — con
 * color Y forma (punto bajo el número), para no depender solo del color.
 */
export function AvailabilityCalendar({
  slots,
  selectedDate,
  onSelectDate,
  today = todayIso(),
}: {
  slots: BookableSlot[]
  selectedDate: IsoDate | null
  onSelectDate: (date: IsoDate) => void
  today?: IsoDate
}) {
  const byDate = useMemo(() => slotsByDate(slots), [slots])
  const bounds = useMemo(() => navigationBounds(byDate, today), [byDate, today])
  const initial = selectedDate ?? firstAvailableDate(byDate, today) ?? today
  const [month, setMonth] = useState<YearMonth>(() => yearMonthOf(initial))

  // Cuando la disponibilidad llega después del primer render, se abre en el mes de la primera fecha.
  const firstDate = firstAvailableDate(byDate, today)
  useEffect(() => {
    if (!selectedDate && firstDate) setMonth(yearMonthOf(firstDate))
    // Solo reacciona a la llegada de datos, no a cada selección.
  }, [firstDate])

  const weeks = buildCalendarMonth(month, byDate, today, selectedDate)
  const daysAvailable = availableDaysInMonth(month, byDate, today)
  const canPrev = canGo(month, -1, bounds)
  const canNext = canGo(month, 1, bounds)

  return (
    <View className="rounded-2xl bg-surface p-4" style={{ elevation: 2 }}>
      <View className="mb-1 flex-row items-center justify-between">
        <MonthArrow label="‹" accessibilityLabel="Mes anterior" disabled={!canPrev} onPress={() => setMonth(addMonths(month, -1))} />
        <Text accessibilityRole="header" className="text-lg font-bold text-ink">
          {monthLabel(month)}
        </Text>
        <MonthArrow label="›" accessibilityLabel="Mes siguiente" disabled={!canNext} onPress={() => setMonth(addMonths(month, 1))} />
      </View>
      <Text className="mb-3 text-center text-xs text-[#5B7285]">
        {daysAvailable === 0
          ? 'Sin fechas disponibles este mes'
          : `${daysAvailable} ${daysAvailable === 1 ? 'día disponible' : 'días disponibles'}`}
      </Text>

      <View className="mb-1 flex-row">
        {WEEKDAY_SHORT_LABELS.map((label) => (
          <Text key={label} className="flex-1 text-center text-xs font-semibold text-[#5B7285]">
            {label}
          </Text>
        ))}
      </View>

      {weeks.map((week) => (
        <View key={week[0].iso} className="flex-row">
          {week.map((day) => (
            <DayCell key={day.iso} day={day} onPress={() => onSelectDate(day.iso)} />
          ))}
        </View>
      ))}

      {daysAvailable === 0 && firstDate ? (
        <Pressable
          accessibilityRole="button"
          onPress={() => setMonth(yearMonthOf(firstDate))}
          className="mt-3 items-center rounded-xl bg-primary/10 py-2.5 active:opacity-70"
        >
          <Text className="text-sm font-semibold text-primary">Ir a la próxima fecha disponible</Text>
        </Pressable>
      ) : null}

      <View className="mt-4 flex-row flex-wrap justify-center gap-x-4 gap-y-1">
        <Legend swatch="bg-secondary/15 border border-secondary" label="Disponible" />
        <Legend swatch="bg-primary" label="Seleccionado" />
        <Legend swatch="bg-[#EEF2F6]" label="Sin cupo / pasado" />
      </View>
    </View>
  )
}

function DayCell({ day, onPress }: { day: CalendarDay; onPress: () => void }) {
  if (day.state === 'outside') return <View className="h-12 flex-1" />

  const interactive = day.state === 'available' || day.state === 'selected'
  const label = longDate.format(parseIsoDate(day.iso))
  const detail =
    day.state === 'past'
      ? 'fecha pasada'
      : day.state === 'unavailable'
        ? 'sin disponibilidad'
        : `${day.slots.length === 1 ? 'disponible' : `${day.slots.length} horarios disponibles`}`

  const circle =
    day.state === 'selected'
      ? 'bg-primary'
      : day.state === 'available'
        ? 'border border-secondary bg-secondary/15'
        : ''
  const text =
    day.state === 'selected'
      ? 'font-bold text-white'
      : day.state === 'available'
        ? 'font-semibold text-ink'
        : day.state === 'past'
          ? 'text-[#CBD5E1]'
          : 'text-[#94A3B8]'

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${label}, ${detail}`}
      accessibilityState={{ disabled: !interactive, selected: day.state === 'selected' }}
      disabled={!interactive}
      onPress={onPress}
      className="h-12 flex-1 items-center justify-center active:opacity-70"
    >
      <View className={`h-10 w-10 items-center justify-center rounded-full ${circle}`}>
        <Text className={`text-base ${text} ${day.state === 'past' ? 'line-through' : ''}`}>{day.day}</Text>
        {day.state === 'available' ? <View className="absolute bottom-1 h-1 w-1 rounded-full bg-secondary" /> : null}
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
      className={`h-10 w-10 items-center justify-center rounded-full ${disabled ? 'opacity-25' : 'bg-background active:opacity-70'}`}
    >
      <Text className="text-2xl text-primary">{label}</Text>
    </Pressable>
  )
}

function Legend({ swatch, label }: { swatch: string; label: string }) {
  return (
    <View className="flex-row items-center gap-1.5">
      <View className={`h-3 w-3 rounded-full ${swatch}`} />
      <Text className="text-xs text-[#5B7285]">{label}</Text>
    </View>
  )
}

