/**
 * Query keys de datos PRIVADOS del turista. Todas cuelgan de `['reservations', userId]`:
 *
 * - el `userId` en la key hace imposible que la sesión de un turista lea la caché de otro, aunque la
 *   limpieza fallara;
 * - el prefijo común `['reservations']` permite borrar todo lo privado de una vez (SessionQuerySync) sin
 *   tocar el catálogo público, que vive bajo `['catalog']`.
 */
export const reservationKeys = {
  all: ['reservations'] as const,
  scope: (userId: string) => ['reservations', userId] as const,
  list: (userId: string) => ['reservations', userId, 'list'] as const,
  detail: (userId: string, id: string) => ['reservations', userId, 'detail', id] as const,
}
