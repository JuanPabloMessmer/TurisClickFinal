/**
 * Vuelos publicados por operadores (Oleada 18): alta de vuelos y salidas, emisión de pasajes y autorización de
 * empresas para venderlos. **Apagado** desde la Oleada 19: los vuelos de TurisClick son un metabuscador que
 * redirige al vendedor, y las agencias sólo administran paquetes. El backend responde 501 a esos endpoints
 * (`Flights:ProviderFlights:Enabled`). Las pantallas siguen en el código, fuera de la navegación y de las rutas.
 */
export const PROVIDER_FLIGHTS_ENABLED = false
