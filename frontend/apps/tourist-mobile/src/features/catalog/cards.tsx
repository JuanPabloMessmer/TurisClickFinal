import type { ExperienceSummaryResponse, PackageSummaryResponse } from '@turisclick/api-client'
import { Link } from 'expo-router'
import { Pressable, Text, View } from 'react-native'
import { CatalogImage, Price, Skeleton } from '@/ui'

/**
 * Tarjetas del catálogo. Todo lo que muestran sale del summary que ya devuelve la búsqueda, así que
 * una lista no dispara una request por ítem.
 */

function CardShell({ children }: { children: React.ReactNode }) {
  return (
    <View className="overflow-hidden rounded-2xl bg-surface shadow-sm" style={{ elevation: 2 }}>
      {children}
    </View>
  )
}

/** Pie común: precio a la izquierda y el dato propio de cada tipo (duración o días) a la derecha. */
function CardFooter({
  amount,
  currency,
  meta,
}: {
  amount?: number | null
  currency?: string | null
  meta?: string | null
}) {
  return (
    <View className="mt-3 flex-row items-center justify-between">
      <Price amount={amount} currency={currency} />
      {meta ? <Text className="text-sm text-[#5B7285]">{meta}</Text> : null}
    </View>
  )
}

export function ExperienceCard({ experience }: { experience: ExperienceSummaryResponse }) {
  return (
    <Link href={`/experience/${experience.id}`} asChild>
      <Pressable accessibilityRole="button" className="active:opacity-90">
        <CardShell>
          <CatalogImage uri={experience.coverImageUrl} className="h-48 w-full" />
          <View className="p-4">
            <Text className="text-xs font-medium uppercase tracking-wide text-secondary">
              {experience.destinationName}
            </Text>
            <Text className="mt-1 text-base font-semibold text-ink" numberOfLines={2}>
              {experience.title}
            </Text>
            {experience.companyName ? (
              <Text className="mt-0.5 text-sm text-[#5B7285]" numberOfLines={1}>
                {experience.companyName}
              </Text>
            ) : null}
            <CardFooter
              amount={experience.price}
              currency={experience.currency}
              meta={experience.durationLabel}
            />
          </View>
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
          <CatalogImage uri={pkg.coverImageUrl} className="h-48 w-full" />
          <View className="p-4">
            <Text className="text-xs font-medium uppercase tracking-wide text-secondary">
              {pkg.destinationName}
            </Text>
            <Text className="mt-1 text-base font-semibold text-ink" numberOfLines={2}>
              {pkg.title}
            </Text>
            {pkg.companyName ? (
              <Text className="mt-0.5 text-sm text-[#5B7285]" numberOfLines={1}>
                {pkg.companyName}
              </Text>
            ) : null}
            <CardFooter
              amount={pkg.price}
              currency={pkg.currency}
              meta={days != null ? `${days} ${days === 1 ? 'día' : 'días'}` : null}
            />
          </View>
        </CardShell>
      </Pressable>
    </Link>
  )
}

/** Misma silueta que las tarjetas reales, para que al cargar no salte el layout. */
export function CardSkeleton() {
  return (
    <CardShell>
      <Skeleton className="h-48 w-full rounded-none" />
      <View className="p-4">
        <Skeleton className="h-3 w-24" />
        <Skeleton className="mt-2 h-4 w-full" />
        <Skeleton className="mt-2 h-3 w-32" />
        <Skeleton className="mt-3 h-5 w-28" />
      </View>
    </CardShell>
  )
}
