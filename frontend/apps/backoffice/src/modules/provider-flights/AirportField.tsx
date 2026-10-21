import { useQuery } from '@tanstack/react-query'
import { flightsApi } from '@turisclick/api-client'
import { useEffect, useId, useState } from 'react'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { httpClient } from '@/lib/httpClient'
import { cn } from '@/lib/utils'

/**
 * Elegir un aeropuerto escribiendo ciudad, nombre, país o código. El origen puede ser cualquier aeropuerto del
 * mundo; el destino, sólo uno de Bolivia: es la misma regla que valida el backend (role=destination).
 */
export function AirportField({
  label,
  role,
  value,
  valueLabel,
  onChange,
  disabled,
  error,
}: {
  label: string
  role: flightsApi.AirportSearchRole
  value: string
  valueLabel?: string | null
  onChange: (iata: string, label: string) => void
  disabled?: boolean
  error?: string
}) {
  const id = useId()
  const [query, setQuery] = useState('')
  const [debounced, setDebounced] = useState('')
  const [open, setOpen] = useState(false)

  useEffect(() => {
    const t = setTimeout(() => setDebounced(query.trim()), 250)
    return () => clearTimeout(t)
  }, [query])

  const results = useQuery({
    queryKey: ['airportSearch', role, debounced],
    queryFn: ({ signal }) => flightsApi.searchAirports(httpClient, { q: debounced, role, limit: 8 }, signal),
    enabled: open && debounced.length >= 2,
    staleTime: 5 * 60_000,
  })

  return (
    <div className="relative flex flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        role="combobox"
        aria-expanded={open}
        aria-controls={`${id}-list`}
        aria-invalid={error ? true : undefined}
        autoComplete="off"
        disabled={disabled}
        placeholder={role === 'destination' ? 'Ciudad o aeropuerto de Bolivia' : 'Ciudad, aeropuerto, país o código'}
        value={open ? query : value ? `${value} · ${valueLabel ?? ''}`.replace(/ · $/, '') : ''}
        onFocus={() => {
          setOpen(true)
          setQuery('')
        }}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        onChange={(event) => setQuery(event.target.value)}
      />
      {open && debounced.length >= 2 && (
        <ul
          id={`${id}-list`}
          role="listbox"
          className="absolute top-full z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-md border border-border bg-surface shadow-lg"
        >
          {results.isPending && <li className="px-3 py-2 text-sm text-muted-foreground">Buscando…</li>}
          {results.isSuccess && results.data.length === 0 && (
            <li className="px-3 py-2 text-sm text-muted-foreground">
              {role === 'destination' ? 'Ningún aeropuerto de Bolivia coincide.' : 'Ningún aeropuerto coincide.'}
            </li>
          )}
          {results.isError && <li className="px-3 py-2 text-sm text-destructive">No se pudo buscar aeropuertos.</li>}
          {results.data?.map((airport) => (
            <li key={airport.iata} role="option" aria-selected={airport.iata === value}>
              <button
                type="button"
                className={cn('flex w-full flex-col px-3 py-2 text-left text-sm hover:bg-muted', airport.iata === value && 'bg-muted')}
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => {
                  onChange(airport.iata ?? '', airport.label ?? airport.name ?? '')
                  setOpen(false)
                }}
              >
                <span className="font-medium text-foreground">
                  {airport.iata} · {airport.city ?? airport.name}
                </span>
                <span className="text-xs text-muted-foreground">
                  {airport.name}
                  {airport.country ? `, ${airport.country}` : ''}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  )
}
