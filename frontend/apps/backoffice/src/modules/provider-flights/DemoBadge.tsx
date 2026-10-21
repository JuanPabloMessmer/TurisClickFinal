import { Badge } from '@/components/ui/badge'

/**
 * Rótulo DEMO de todo lo que es vuelo de operador en este entorno: un vuelo cargado o un pasaje registrado por
 * un operador de prueba no es un vuelo ni un pasaje reales, y la pantalla no los presenta como tales.
 */
export function DemoBadge({ className }: { className?: string }) {
  return (
    <Badge variant="warning" className={className} title="Entorno de demostración: no es un vuelo ni un pasaje reales">
      DEMO
    </Badge>
  )
}
