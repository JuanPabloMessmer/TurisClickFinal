import { addDaysIso, todayIso } from '@turisclick/utils'
import { useRouter } from 'expo-router'
import { ArrowUpDown, CalendarDays, PlaneLanding, PlaneTakeoff, Search, Users } from 'lucide-react-native'
import { useState } from 'react'
import { Pressable, ScrollView, Text, View } from 'react-native'
import { AirportPickerModal } from '@/features/flights/search/AirportPickerModal'
import { DateRangeModal, formatShortDate } from '@/features/flights/search/DateRangeModal'
import {
  cabinLabel,
  canSwap,
  draftProblem,
  initialDraft,
  passengersSummary,
  toSearchParams,
  type AirportChoice,
  type SearchDraft,
} from '@/features/flights/search/model'
import { PassengersModal } from '@/features/flights/search/PassengersModal'
import { colors } from '@/theme/colors'
import { Button, Icon, SegmentedControl, Surface } from '@/ui'

type Panel = 'origin' | 'destination' | 'dates' | 'passengers' | null

/**
 * Buscador de vuelos, con la forma de cualquier buscador conocido: origen y destino con autocompletado,
 * ida y vuelta o sólo ida en un calendario, pasajeros y cabina, y un botón. Los resultados se abren en otra
 * pantalla; al volver, el formulario sigue como estaba.
 *
 * Origen: cualquier aeropuerto del mundo. Destino: por ahora, Bolivia. Las reglas viven en
 * `search/model.ts` y son las mismas que valida el backend.
 *
 * Esta pantalla **no vende pasajes**: la compra de vuelos sueltos todavía no existe, y los resultados lo
 * dicen. Lo que sí se puede comprar hoy son los paquetes que vuelan la ruta.
 */
export function FlightSearchScreen({ today = todayIso() }: { today?: string }) {
  const router = useRouter()
  const [draft, setDraft] = useState<SearchDraft>(() => initialDraft(today))
  const [panel, setPanel] = useState<Panel>(null)

  const problem = draftProblem(draft, today)
  const close = () => setPanel(null)

  const chooseAirport = (side: 'origin' | 'destination', airport: AirportChoice) => {
    setDraft((d) => ({ ...d, [side]: airport }))
    setPanel(null)
  }

  const search = () => {
    if (problem) return
    router.push({ pathname: '/flight-results', params: toSearchParams(draft) })
  }

  return (
    <ScrollView contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 32 }} keyboardShouldPersistTaps="handled">
      <Text className="font-sans text-body text-ink-muted">Vuelos a Bolivia desde cualquier aeropuerto del mundo.</Text>

      <Surface className="mt-4 rounded-lg">
        <View className="p-4">
          <SegmentedControl
            options={[
              { value: 'ROUND_TRIP', label: 'Ida y vuelta' },
              { value: 'ONE_WAY', label: 'Sólo ida' },
            ]}
            value={draft.tripType}
            onChange={(tripType) =>
              setDraft((d) => ({
                ...d,
                tripType,
                // Pasar a ida y vuelta propone una vuelta a la semana, en vez de dejar el buscador incompleto.
                returnDate:
                  tripType === 'ROUND_TRIP'
                    ? d.returnDate ?? (d.departureDate ? addDaysIso(d.departureDate, 7) : null)
                    : d.returnDate,
              }))
            }
          />

          <View className="mt-4">
            <Field
              testID="campo-origen"
              icon={PlaneTakeoff}
              label="Desde"
              value={draft.origin ? `${draft.origin.city} (${draft.origin.iata})` : null}
              detail={draft.origin?.country}
              placeholder="Ciudad o aeropuerto de salida"
              onPress={() => setPanel('origin')}
            />
            <View className="relative">
              <View className="ml-11 h-px bg-border" />
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Invertir origen y destino"
                accessibilityState={{ disabled: !canSwap(draft) }}
                disabled={!canSwap(draft)}
                onPress={() => setDraft((d) => ({ ...d, origin: d.destination, destination: d.origin }))}
                className={`absolute right-2 -top-5 h-10 w-10 items-center justify-center rounded-full border border-border bg-surface ${
                  canSwap(draft) ? 'active:opacity-60' : 'opacity-30'
                }`}
              >
                <Icon icon={ArrowUpDown} size={16} color={colors.primary} />
              </Pressable>
            </View>
            <Field
              testID="campo-destino"
              icon={PlaneLanding}
              label="Hacia"
              value={draft.destination ? `${draft.destination.city} (${draft.destination.iata})` : null}
              detail={draft.destination?.country}
              placeholder="Destino en Bolivia"
              onPress={() => setPanel('destination')}
            />
          </View>

          <View className="mt-2 border-t border-border">
            <Field
              testID="campo-fechas"
              icon={CalendarDays}
              label={draft.tripType === 'ROUND_TRIP' ? 'Ida y vuelta' : 'Ida'}
              value={
                draft.tripType === 'ROUND_TRIP'
                  ? `${formatShortDate(draft.departureDate)} – ${formatShortDate(draft.returnDate)}`
                  : formatShortDate(draft.departureDate)
              }
              onPress={() => setPanel('dates')}
            />
          </View>

          <View className="border-t border-border">
            <Field
              testID="campo-pasajeros"
              icon={Users}
              label="Pasajeros y cabina"
              value={`${passengersSummary(draft.passengers)} · ${cabinLabel(draft.cabin)}`}
              onPress={() => setPanel('passengers')}
            />
          </View>

          <View className="mt-4">
            <Button label="Buscar vuelos" icon={Search} disabled={Boolean(problem)} onPress={search} />
            {problem && (draft.origin || draft.destination) ? (
              <Text accessibilityRole="alert" className="mt-2 font-sans text-label text-danger-fg">
                {problem}
              </Text>
            ) : null}
          </View>
        </View>
      </Surface>

      <Text className="mt-5 font-sans text-caption text-ink-muted">
        Buscás vuelos y los comparás. Comprar un vuelo suelto todavía no está disponible: hoy el vuelo se compra
        dentro de un paquete de TurisClick.
      </Text>

      <AirportPickerModal
        visible={panel === 'origin'}
        role="origin"
        exclude={draft.destination?.iata}
        onSelect={(airport) => chooseAirport('origin', airport)}
        onClose={close}
      />
      <AirportPickerModal
        visible={panel === 'destination'}
        role="destination"
        exclude={draft.origin?.iata}
        onSelect={(airport) => chooseAirport('destination', airport)}
        onClose={close}
      />
      <DateRangeModal
        visible={panel === 'dates'}
        tripType={draft.tripType}
        value={{ departure: draft.departureDate, return: draft.returnDate }}
        today={today}
        onConfirm={(range) => {
          setDraft((d) => ({ ...d, departureDate: range.departure, returnDate: range.return }))
          setPanel(null)
        }}
        onClose={close}
      />
      <PassengersModal
        visible={panel === 'passengers'}
        value={draft.passengers}
        cabin={draft.cabin}
        onConfirm={(passengers, cabin) => {
          setDraft((d) => ({ ...d, passengers, cabin }))
          setPanel(null)
        }}
        onClose={close}
      />
    </ScrollView>
  )
}

function Field({
  testID,
  icon,
  label,
  value,
  detail,
  placeholder,
  onPress,
}: {
  testID: string
  icon: Parameters<typeof Icon>[0]['icon']
  label: string
  value: string | null
  detail?: string
  placeholder?: string
  onPress: () => void
}) {
  return (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={`${label}: ${value ?? placeholder ?? 'sin elegir'}`}
      onPress={onPress}
      className="min-h-[56px] flex-row items-center gap-3 py-2 pr-12 active:opacity-60"
    >
      <View className="h-8 w-8 items-center justify-center">
        <Icon icon={icon} size={20} color={colors.primary} />
      </View>
      <View className="flex-1">
        <Text className="font-ui600 text-caption uppercase tracking-wide text-ink-muted">{label}</Text>
        <Text className={`font-ui600 text-body ${value ? 'text-ink' : 'text-ink-muted'}`} numberOfLines={1}>
          {value ?? placeholder}
        </Text>
        {detail ? <Text className="font-sans text-caption text-ink-muted">{detail}</Text> : null}
      </View>
    </Pressable>
  )
}
