import type { UseInfiniteQueryResult } from '@tanstack/react-query'
import { useMemo } from 'react'
import { FlatList, RefreshControl, View } from 'react-native'
import { CardSkeleton } from '@/features/catalog/cards'
import { toApiError } from '@/lib/errors'
import { colors } from '@/theme/colors'
import { EmptyState, ErrorState, ListFooterLoader } from '@/ui'

/**
 * Lista paginada del catálogo con sus cuatro estados: cargando, error, vacía y con datos. Es genérica
 * sobre el tipo del ítem para que experiencias y paquetes compartan el comportamiento (scroll infinito,
 * pull-to-refresh, reintento) sin castear el elemento en el `renderItem`.
 */
export interface PagedResponse<TItem> {
  items?: TItem[] | null
  page?: number
  totalPages?: number
  totalCount?: number
}

export function CatalogList<TItem extends { id?: string }>({
  query,
  renderItem,
  emptyTitle,
  emptyMessage,
}: {
  query: UseInfiniteQueryResult<{ pages: PagedResponse<TItem>[] }, Error>
  renderItem: (item: TItem) => React.ReactElement
  emptyTitle: string
  emptyMessage: string
}) {
  const items = useMemo(() => query.data?.pages.flatMap((page) => page.items ?? []) ?? [], [query.data])

  if (query.isError) {
    return <ErrorState message={toApiError(query.error).message} onRetry={query.refetch} />
  }

  if (query.isPending) {
    return (
      <View className="gap-4 px-5">
        {[0, 1, 2].map((key) => (
          <CardSkeleton key={key} />
        ))}
      </View>
    )
  }

  if (items.length === 0) {
    return <EmptyState title={emptyTitle} message={emptyMessage} />
  }

  return (
    <FlatList
      data={items}
      keyExtractor={(item) => String(item.id)}
      contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 24, gap: 16 }}
      showsVerticalScrollIndicator={false}
      // Se dispara a media pantalla del final: da tiempo a que la página entre antes de tocar fondo.
      onEndReachedThreshold={0.5}
      onEndReached={() => {
        if (query.hasNextPage && !query.isFetchingNextPage) void query.fetchNextPage()
      }}
      ListFooterComponent={<ListFooterLoader visible={query.isFetchingNextPage} />}
      refreshControl={
        <RefreshControl
          refreshing={query.isRefetching && !query.isFetchingNextPage}
          onRefresh={() => void query.refetch()}
          tintColor={colors.primary}
          colors={[colors.primary]}
        />
      }
      renderItem={({ item }) => renderItem(item)}
    />
  )
}
