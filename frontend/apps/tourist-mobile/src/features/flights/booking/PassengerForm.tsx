import { Text, View } from 'react-native'
import {
  passengerTypeLabel,
  type PassengerDraft,
  type PassengerErrors,
  type PassengerSlot,
} from '@/features/flights/booking/model'
import { TextField } from '@/ui'

/**
 * Un pasajero de un vuelo de operador: nombre, apellido y fecha de nacimiento, que es lo mínimo que el
 * operador necesita para emitir. **Sin documento de identidad**, y en la demo con datos ficticios.
 */
export function PassengerForm({
  index,
  slot,
  passenger,
  errors,
  onChange,
}: {
  index: number
  slot: PassengerSlot
  passenger: PassengerDraft
  errors: PassengerErrors
  onChange: (passenger: PassengerDraft) => void
}) {
  const set = (key: keyof PassengerDraft, value: string) => onChange({ ...passenger, [key]: value })
  const n = index + 1

  return (
    <View testID={`passenger-form-${index}`} className="gap-4 rounded-lg border border-border bg-surface p-4">
      <View>
        <Text className="font-ui700 text-label text-ink">Pasajero {n}</Text>
        <Text className="mt-0.5 font-sans text-caption text-ink-muted">{passengerTypeLabel(slot)}</Text>
      </View>

      <TextField
        label={`Nombre del pasajero ${n}`}
        placeholder="Ana"
        value={passenger.givenName}
        error={errors.givenName}
        autoCapitalize="words"
        autoComplete="off"
        onChangeText={(value) => set('givenName', value)}
      />

      <TextField
        label={`Apellido del pasajero ${n}`}
        placeholder="Prueba"
        value={passenger.familyName}
        error={errors.familyName}
        autoCapitalize="words"
        autoComplete="off"
        onChangeText={(value) => set('familyName', value)}
      />

      <TextField
        label={`Fecha de nacimiento del pasajero ${n}`}
        placeholder="1990-05-14"
        value={passenger.bornOn}
        error={errors.bornOn}
        keyboardType="numbers-and-punctuation"
        onChangeText={(value) => set('bornOn', value)}
      />
    </View>
  )
}
