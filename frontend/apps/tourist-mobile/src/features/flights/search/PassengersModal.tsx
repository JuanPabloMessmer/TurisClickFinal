import { useState } from 'react'
import { Pressable, ScrollView, Text, View } from 'react-native'
import {
  CABINS,
  CHILD_AGES,
  infantBlockReason,
  MAX_PASSENGERS,
  passengerLimits,
  totalPassengers,
  type Cabin,
  type PassengerMix,
} from '@/features/flights/search/model'
import { SheetModal } from '@/features/flights/search/SheetModal'
import { Button, Chip } from '@/ui'

/** Mientras se edita, la edad de un niño recién sumado todavía no se eligió. */
type DraftMix = Omit<PassengerMix, 'childrenAges'> & { childrenAges: (number | null)[] }

/**
 * Pasajeros y cabina. Los límites son los de los proveedores aéreos y los mismos que valida el backend:
 * hasta 9 pasajeros, al menos un adulto, no más infantes que adultos (cada uno viaja en brazos de alguien) y
 * la edad de cada niño, que es lo que el proveedor necesita para tarifarlo. Cuando un botón no se puede
 * usar, se dice por qué en vez de ignorar el toque.
 */
export function PassengersModal({
  visible,
  value,
  cabin,
  onConfirm,
  onClose,
}: {
  visible: boolean
  value: PassengerMix
  cabin: Cabin
  onConfirm: (passengers: PassengerMix, cabin: Cabin) => void
  onClose: () => void
}) {
  const [mix, setMix] = useState<DraftMix>(value)
  const [selectedCabin, setSelectedCabin] = useState<Cabin>(cabin)

  const [lastVisible, setLastVisible] = useState(visible)
  if (visible !== lastVisible) {
    setLastVisible(visible)
    if (visible) {
      setMix(value)
      setSelectedCabin(cabin)
    }
  }

  // Para los límites, un niño sin edad elegida cuenta igual: ya ocupa un asiento.
  const counted: PassengerMix = { ...mix, childrenAges: mix.childrenAges.map((age) => age ?? 0) }
  const limits = passengerLimits(counted)
  const missingAge = mix.childrenAges.some((age) => age === null)
  const infantReason = infantBlockReason(counted)

  const confirm = () => {
    if (missingAge) return
    onConfirm({ ...mix, childrenAges: mix.childrenAges as number[] }, selectedCabin)
  }

  return (
    <SheetModal
      visible={visible}
      title="Pasajeros y cabina"
      onClose={onClose}
      footer={
        <Button
          label={missingAge ? 'Elegí la edad de cada niño' : 'Listo'}
          disabled={missingAge}
          onPress={confirm}
        />
      }
    >
      <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 32 }}>
        <Stepper
          label="Adultos"
          hint="12 años o más"
          noun="adulto"
          value={mix.adults}
          canAdd={limits.canAddAdult}
          canRemove={limits.canRemoveAdult}
          onAdd={() => setMix((m) => ({ ...m, adults: m.adults + 1 }))}
          onRemove={() => setMix((m) => ({ ...m, adults: m.adults - 1 }))}
        />
        <Stepper
          label="Niños"
          hint="De 2 a 11 años, con asiento"
          noun="niño"
          value={mix.childrenAges.length}
          canAdd={limits.canAddChild}
          canRemove={limits.canRemoveChild}
          onAdd={() => setMix((m) => ({ ...m, childrenAges: [...m.childrenAges, null] }))}
          onRemove={() => setMix((m) => ({ ...m, childrenAges: m.childrenAges.slice(0, -1) }))}
        />

        {mix.childrenAges.map((age, index) => (
          <View key={index} className="mb-4 ml-1">
            <Text className="font-ui600 text-label text-ink">Edad del niño {index + 1}</Text>
            <ScrollView horizontal showsHorizontalScrollIndicator={false} className="mt-2">
              <View accessibilityLabel={`Edad del niño ${index + 1}`} className="flex-row gap-2 pr-5">
                {CHILD_AGES.map((option) => (
                  <Chip
                    key={option}
                    label={`${option} años`}
                    selected={age === option}
                    onPress={() =>
                      setMix((m) => ({
                        ...m,
                        childrenAges: m.childrenAges.map((current, i) => (i === index ? option : current)),
                      }))
                    }
                  />
                ))}
              </View>
            </ScrollView>
          </View>
        ))}

        <Stepper
          label="Infantes"
          hint="Menores de 2 años, en brazos"
          noun="infante"
          value={mix.infants}
          canAdd={limits.canAddInfant}
          canRemove={limits.canRemoveInfant}
          onAdd={() => setMix((m) => ({ ...m, infants: m.infants + 1 }))}
          onRemove={() => setMix((m) => ({ ...m, infants: m.infants - 1 }))}
        />
        {infantReason && !limits.full ? (
          <Text className="-mt-2 mb-3 font-sans text-caption text-ink-muted">{infantReason}</Text>
        ) : null}

        {limits.full ? (
          <Text accessibilityRole="alert" className="mb-3 font-sans text-caption text-ink-muted">
            Se pueden buscar hasta {MAX_PASSENGERS} pasajeros por reserva ({totalPassengers(counted)} elegidos).
          </Text>
        ) : null}

        <Text className="mb-2 mt-4 font-ui700 text-heading text-ink">Cabina</Text>
        <View className="flex-row flex-wrap gap-2">
          {CABINS.map((option) => (
            <Chip
              key={option.value}
              label={option.label}
              selected={selectedCabin === option.value}
              onPress={() => setSelectedCabin(option.value)}
            />
          ))}
        </View>
        <Text className="mt-2 font-sans text-caption text-ink-muted">
          Si la aerolínea no vende esa cabina en la ruta, la búsqueda no devuelve vuelos para ella.
        </Text>
      </ScrollView>
    </SheetModal>
  )
}

function Stepper({
  label,
  hint,
  noun,
  value,
  canAdd,
  canRemove,
  onAdd,
  onRemove,
}: {
  label: string
  hint: string
  noun: string
  value: number
  canAdd: boolean
  canRemove: boolean
  onAdd: () => void
  onRemove: () => void
}) {
  return (
    <View className="mb-4 flex-row items-center justify-between border-b border-border pb-4">
      <View className="flex-1">
        <Text className="font-ui600 text-body text-ink">{label}</Text>
        <Text className="font-sans text-caption text-ink-muted">{hint}</Text>
      </View>
      <View className="flex-row items-center gap-3">
        <StepButton symbol="−" accessibilityLabel={`Quitar ${noun}`} disabled={!canRemove} onPress={onRemove} />
        <Text accessibilityLabel={`${label}: ${value}`} className="w-6 text-center font-ui700 text-title text-ink">
          {value}
        </Text>
        <StepButton symbol="+" accessibilityLabel={`Sumar ${noun}`} disabled={!canAdd} onPress={onAdd} />
      </View>
    </View>
  )
}

function StepButton({
  symbol,
  accessibilityLabel,
  disabled,
  onPress,
}: {
  symbol: string
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
      className={`h-11 w-11 items-center justify-center rounded-full border ${
        disabled ? 'border-border opacity-40' : 'border-primary active:opacity-60'
      }`}
    >
      <Text className="text-xl text-primary">{symbol}</Text>
    </Pressable>
  )
}
