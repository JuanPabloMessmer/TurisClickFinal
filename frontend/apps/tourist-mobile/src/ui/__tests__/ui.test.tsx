import { fireEvent, render, screen } from '@testing-library/react-native'
import { Image } from 'react-native'
import { Badge, Button, CatalogImage, FormError, Price, SegmentedControl, TextField } from '@/ui'

describe('Price', () => {
  it('muestra monto y moneda con dos decimales', () => {
    render(<Price amount={1250.5} currency="BOB" />)

    expect(screen.getByText(/BOB\s+1250\.50/)).toBeTruthy()
  })

  it('no inventa un 0 cuando el monto no vino', () => {
    render(<Price amount={undefined} currency="BOB" />)

    expect(screen.getByText('Consultar precio')).toBeTruthy()
  })

  it('trata el 0 como un precio real, no como ausencia', () => {
    render(<Price amount={0} currency="USD" />)

    expect(screen.queryByText('Consultar precio')).toBeNull()
    expect(screen.getByText(/USD\s+0\.00/)).toBeTruthy()
  })
})

describe('Button', () => {
  it('no dispara onPress cuando está deshabilitado', () => {
    const onPress = jest.fn()
    render(<Button label="Reservar" disabled onPress={onPress} />)

    fireEvent.press(screen.getByText('Reservar'))

    expect(onPress).not.toHaveBeenCalled()
  })

  it('expone el estado deshabilitado a accesibilidad, no solo con opacidad', () => {
    render(<Button label="Reservar" disabled />)

    expect(screen.getByRole('button').props.accessibilityState).toMatchObject({ disabled: true })
  })

  it('mientras carga se bloquea y esconde el label', () => {
    const onPress = jest.fn()
    render(<Button label="Entrar" loading onPress={onPress} />)

    expect(screen.queryByText('Entrar')).toBeNull()
    expect(screen.getByRole('button').props.accessibilityState).toMatchObject({ disabled: true, busy: true })
  })
})

describe('TextField', () => {
  it('muestra el mensaje de error además del borde rojo', () => {
    render(<TextField label="Email" error="Ingresá un email válido" />)

    expect(screen.getByText('Ingresá un email válido')).toBeTruthy()
  })

  it('usa el label como etiqueta accesible del input', () => {
    render(<TextField label="Contraseña" />)

    expect(screen.getByLabelText('Contraseña')).toBeTruthy()
  })
})

describe('FormError', () => {
  it('no renderiza nada sin mensaje', () => {
    const { toJSON } = render(<FormError message={null} />)

    expect(toJSON()).toBeNull()
  })

  it('se anuncia como alerta cuando hay mensaje', () => {
    render(<FormError message="Credenciales inválidas" />)

    expect(screen.getByRole('alert')).toBeTruthy()
  })
})

describe('CatalogImage', () => {
  it('muestra el placeholder cuando el producto no tiene foto', () => {
    render(<CatalogImage uri={null} />)

    expect(screen.getByText('🏔️')).toBeTruthy()
  })

  it('intenta la foto cuando hay URL', () => {
    render(<CatalogImage uri="https://cdn.example.com/foto.jpg" />)

    expect(screen.queryByText('🏔️')).toBeNull()
  })

  /**
   * Encontrado ejecutando la app: las fotos sembradas apuntan a example.com y no cargan, y sin este
   * fallback queda un hueco en blanco del alto de la tarjeta, que se lee como un error de la app.
   */
  it('cae al placeholder si la foto existe pero no carga', () => {
    render(<CatalogImage uri="https://example.com/rota.jpg" />)

    fireEvent(screen.UNSAFE_getByType(Image), 'error')

    expect(screen.getByText('🏔️')).toBeTruthy()
  })

  it('vuelve a intentar si el producto cambia de foto', () => {
    const { rerender } = render(<CatalogImage uri="https://example.com/rota.jpg" />)
    fireEvent(screen.UNSAFE_getByType(Image), 'error')
    expect(screen.getByText('🏔️')).toBeTruthy()

    rerender(<CatalogImage uri="https://cdn.example.com/nueva.jpg" />)

    expect(screen.queryByText('🏔️')).toBeNull()
  })
})

describe('SegmentedControl', () => {
  const options = [
    { value: 'experiences' as const, label: 'Experiencias' },
    { value: 'packages' as const, label: 'Paquetes' },
  ]

  it('marca la opción activa para accesibilidad, no solo con color', () => {
    render(<SegmentedControl options={options} value="packages" onChange={jest.fn()} />)

    const tabs = screen.getAllByRole('tab')
    expect(tabs[0].props.accessibilityState).toMatchObject({ selected: false })
    expect(tabs[1].props.accessibilityState).toMatchObject({ selected: true })
  })

  it('avisa del cambio con el valor, no con el índice', () => {
    const onChange = jest.fn()
    render(<SegmentedControl options={options} value="experiences" onChange={onChange} />)

    fireEvent.press(screen.getByText('Paquetes'))

    expect(onChange).toHaveBeenCalledWith('packages')
  })
})

describe('Badge', () => {
  it('no ocupa lugar cuando no hay filtros puestos', () => {
    const { toJSON } = render(<Badge count={0} />)

    expect(toJSON()).toBeNull()
  })

  it('muestra la cantidad', () => {
    render(<Badge count={3} />)

    expect(screen.getByText('3')).toBeTruthy()
  })
})
