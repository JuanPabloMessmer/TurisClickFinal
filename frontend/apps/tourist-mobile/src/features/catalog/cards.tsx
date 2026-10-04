import type { ExperienceSummaryResponse, PackageSummaryResponse } from '@turisclick/api-client'
import { Link } from 'expo-router'
import { CalendarDays } from 'lucide-react-native'
import { Pressable, Text, View } from 'react-native'
import { nextDepartures, shortDateLabel, slotsLabel, type Departure } from '@/features/catalog/departures'
import { useExperienceAvailability } from '@/features/catalog/queries'
import { colors } from '@/theme/colors'
import { CatalogImage, Icon, Price, Skeleton, Surface } from '@/ui'

/**
 * Tarjetas del catálogo. El resto de los datos sale del summary que ya devuelve la búsqueda, así que
 * una lista no dispara una request por ítem; la excepción consciente es la tira de salidas (abajo).
 */

function CardShell({ children }: { children: React.ReactNode }) {
  return (
    <Surface className="overflow-hidden rounded-lg" level="raised">
      {children}
    </Surface>
  )
}

/**
 * Tira de próximas salidas: fecha real y cupo real, que es lo que decide el viaje.
 *
 * El summary del backend no trae disponibilidad, así que se consulta por producto. Es una request por
 * tarjeta visible —acotada a las pocas que entran en un carrusel y cacheada por React Query, que
 * además deja el detalle ya cargado al abrirlo—. Cuando el backend exponga la próxima fecha en el
 * summary, esto pasa a ser un solo request (anotado en docs/ui-debt.md).
 */
function DepartureStrip({ experienceId }: { experienceId: string }) {
  const availability = useExperienceAvailability(experienceId)

  if (availability.isPending) return <Skeleton className="mt-3 h-4 w-40" />

  const departures = nextDepartures(availability.data)
  if (departures.length === 0) return null

  return (
    <View className="mt-3 flex-row items-center gap-2">
      <Icon icon={CalendarDays} size={14} color={colors.inkMuted} />
      <View className="flex-1">
        {departures.map((departure: Departure) => (
          <Text key={departure.id} className="font-ui500 text-caption text-ink-muted" numberOfLines={1}>
            <Text className="text-ink">{shortDateLabel(departure.date)}</Text>
            {`  ·  ${slotsLabel(departure.availableSlots)}`}
          </Text>
        ))}
      </View>
    </View>
  )
}

function CardBody({
  destination,
  title,
  company,
  amount,
  currency,
  meta,
  children,
}: {
  destination?: string | null
  title?: string | null
  company?: string | null
  amount?: number | null
  currency?: string | null
  meta?: string | null
  children?: React.ReactNode
}) {
  return (
    <View className="p-4">
      <Text className="font-ui600 text-caption text-primary" numberOfLines={1}>
        {destination}
      </Text>
      <Text className="mt-1 font-ui600 text-heading text-ink" numberOfLines={2}>
        {title}
      </Text>
      {company ? (
        <Text className="mt-0.5 font-sans text-label text-ink-muted" numberOfLines={1}>
          {company}
        </Text>
      ) : null}

      {children}

      <View className="mt-3 flex-row items-end justify-between">
        <Price amount={amount} currency={currency} />
        {meta ? <Text className="font-sans text-label text-ink-muted">{meta}</Text> : null}
      </View>
    </View>
  )
}

export function ExperienceCard({ experience }: { experience: ExperienceSummaryResponse }) {
  return (
    <Link href={`/experience/${experience.id}`} asChild>
      <Pressable accessibilityRole="button" className="active:opacity-90">
        <CardShell>
          <CatalogImage
            uri={experience.coverImageUrl}
            className="h-44 w-full"
            fallbackLabel={experience.destinationName}
          />
          <CardBody
            destination={experience.destinationName}
            title={experience.title}
            company={experience.companyName}
            amount={experience.price}
            currency={experience.currency}
            meta={experience.durationLabel}
          >
            {experience.id ? <DepartureStrip experienceId={experience.id} /> : null}
          </CardBody>
        </CardShell>
      </Pressable>
    </Link>
  )
}

export function PackageCard({ package: pkg }: { package: PackageSummaryResponse }) {
  const days = pkg.durationDays
  return (
    <Link href={`/package/${pkg.id}`} asChild>
      <Pressable accessibilityRole="button" className="active:opacity-90">
        <CardShell>
          <CatalogImage uri={pkg.coverImageUrl} className="h-44 w-full" fallbackLabel={pkg.destinationName} />
          <CardBody
            destination={pkg.destinationName}
            title={pkg.title}
            company={pkg.companyName}
            amount={pkg.price}
            currency={pkg.currency}
            meta={days != null ? `${days} ${days === 1 ? 'día' : 'días'}` : null}
          />
        </CardShell>
      </Pressable>
    </Link>
  )
}

/** Misma silueta que las tarjetas reales, para que al cargar no salte el layout. */
export function CardSkeleton() {
  return (
    <CardShell>
      <Skeleton className="h-44 w-full rounded-none" />
      <View className="p-4">
        <Skeleton className="h-3 w-24" />
        <Skeleton className="mt-2 h-4 w-full" />
        <Skeleton className="mt-2 h-3 w-32" />
        <Skeleton className="mt-3 h-5 w-28" />
      </View>
    </CardShell>
  )
}
