import type { FlightTravelerRequest } from '@turisclick/api-client'
import { Text, View } from 'react-native'
import { FieldLabel, SegmentedControl, TextField } from '@/ui'

/**
 * Datos de los pasajeros del vuelo.
 *
 * Los campos son los que la aerolínea exige para emitir, y ninguno más. No se pide pasaporte: un vuelo
 * doméstico no lo necesita, y cuando una oferta sí lo exige el backend avisa que esa opción todavía no se
 * puede vender en vez de pedirle el documento a todo el mundo por las dudas.
 *
 * Nada de esto se guarda en TurisClick: se manda al proveedor y se descarta.
 */

export type TravelerDraft = {
  givenName: string
  familyName: string
  bornOn: string
  gender: 'f' | 'm'
  title: 'ms' | 'mr'
  email: string
  phoneNumber: string
}

export const emptyTraveler = (): TravelerDraft => ({
  givenName: '',
  familyName: '',
  bornOn: '',
  gender: 'f',
  title: 'ms',
  email: '',
  phoneNumber: '',
})

const NAME = /^\p{L}[\p{L} '\-.]*$/u
const DATE = /^\d{4}-\d{2}-\d{2}$/
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/
const PHONE = /^\+[1-9]\d{6,14}$/

export type TravelerErrors = Partial<Record<keyof TravelerDraft, string>>

/**
 * Se valida acá lo mismo que valida el backend, por una razón de trato y no de seguridad: la autoridad
 * sigue siendo el servidor, pero hacerle corregir un nombre a alguien DESPUÉS de cobrarle sería maltratarlo.
 */
export function validateTraveler(traveler: TravelerDraft): TravelerErrors {
  const errors: TravelerErrors = {}

  if (traveler.givenName.trim().length < 2 || !NAME.test(traveler.givenName.trim()))
    errors.givenName = 'Escribí el nombre tal como figura en el documento, sin números.'

  if (traveler.familyName.trim().length < 2 || !NAME.test(traveler.familyName.trim()))
    errors.familyName = 'Escribí el apellido tal como figura en el documento, sin números.'

  if (!DATE.test(traveler.bornOn)) errors.bornOn = 'Usá el formato año-mes-día, por ejemplo 1990-05-14.'
  else if (new Date(traveler.bornOn).getTime() >= Date.now()) errors.bornOn = 'La fecha tiene que estar en el pasado.'

  if (!EMAIL.test(traveler.email.trim())) errors.email = 'Revisá el correo: la aerolínea avisa ahí si hay un cambio.'

  if (!PHONE.test(traveler.phoneNumber.trim()))
    errors.phoneNumber = 'Escribilo en formato internacional, por ejemplo +59170000000.'

  return errors
}

export const travelerToRequest = (traveler: TravelerDraft): FlightTravelerRequest => ({
  givenName: traveler.givenName.trim(),
  familyName: traveler.familyName.trim(),
  bornOn: traveler.bornOn,
  gender: traveler.gender,
  title: traveler.title,
  email: traveler.email.trim(),
  phoneNumber: traveler.phoneNumber.trim(),
})

export function TravelerForm({
  index,
  total,
  traveler,
  errors,
  onChange,
}: {
  index: number
  total: number
  traveler: TravelerDraft
  errors: TravelerErrors
  onChange: (traveler: TravelerDraft) => void
}) {
  const set = <K extends keyof TravelerDraft>(key: K, value: TravelerDraft[K]) =>
    onChange({ ...traveler, [key]: value })

  return (
    <View className="gap-4 rounded-lg border border-border bg-surface p-4">
      <Text className="font-ui700 text-label text-ink">
        {total === 1 ? 'Datos del pasajero' : `Pasajero ${index + 1} de ${total}`}
      </Text>

      <TextField
        label="Nombre"
        value={traveler.givenName}
        error={errors.givenName}
        autoCapitalize="words"
        autoComplete="given-name"
        onChangeText={(value) => set('givenName', value)}
      />

      <TextField
        label="Apellido"
        value={traveler.familyName}
        error={errors.familyName}
        autoCapitalize="words"
        autoComplete="family-name"
        onChangeText={(value) => set('familyName', value)}
      />

      <TextField
        label="Fecha de nacimiento"
        placeholder="1990-05-14"
        value={traveler.bornOn}
        error={errors.bornOn}
        keyboardType="numbers-and-punctuation"
        onChangeText={(value) => set('bornOn', value)}
      />

      <View>
        <FieldLabel label="Tratamiento" hint="Como figura en el documento de viaje." />
        <SegmentedControl
          options={[
            { value: 'ms', label: 'Sra./Srta.' },
            { value: 'mr', label: 'Sr.' },
          ]}
          value={traveler.title}
          onChange={(value) => onChange({ ...traveler, title: value, gender: value === 'mr' ? 'm' : 'f' })}
        />
      </View>

      <TextField
        label="Correo"
        placeholder="nombre@correo.com"
        value={traveler.email}
        error={errors.email}
        autoCapitalize="none"
        keyboardType="email-address"
        autoComplete="email"
        onChangeText={(value) => set('email', value)}
      />

      <TextField
        label="Teléfono"
        placeholder="+59170000000"
        value={traveler.phoneNumber}
        error={errors.phoneNumber}
        keyboardType="phone-pad"
        onChangeText={(value) => set('phoneNumber', value)}
      />

      {index === total - 1 ? (
        <Text className="font-sans text-caption text-ink-muted">
          Estos datos se usan sólo para emitir el pasaje con la aerolínea. TurisClick no los guarda.
        </Text>
      ) : null}
    </View>
  )
}
