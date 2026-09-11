/**
 * El detalle devuelve `images[]` en vez del `coverImageUrl` que trae el summary, así que la portada se
 * resuelve acá: la marcada como cover, y si ninguna lo está (o la que lo está vino sin url), la primera
 * por `sortOrder`.
 */
export function coverImageUrl(images?: { url?: string | null; sortOrder?: number; isCover?: boolean }[] | null) {
  const usable = (images ?? []).filter((image) => Boolean(image.url))
  if (usable.length === 0) return null

  const cover = usable.find((image) => image.isCover)
  if (cover) return cover.url ?? null

  return [...usable].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))[0].url ?? null
}
