import { fireEvent, render, screen } from '@testing-library/react-native'
import { StyleSheet } from 'react-native'
import { Badge, Button, CatalogImage, FormError, Price, SegmentedControl, TextField } from '@/ui'

describe('Price', () => {
  it('muestra monto y moneda con dos decimales', async () => {
    await render(<Price amount={1250.5} currency="BOB" />)

    expect(screen.getByText(/Bs\s+1\.250,50/)).toBeTruthy()
  })

  it('no inventa un 0 cuando el monto no vino', async () => {
    await render(<Price amount={undefined} currency="BOB" />)

    expect(screen.getByText('Consultar precio')).toBeTruthy()
  })

  it('trata el 0 como un precio real, no como ausencia', async () => {
    await render(<Price amount={0} currency="USD" />)

    expect(screen.queryByText('Consultar precio')).toBeNull()
    expect(screen.getByText(/USD\s+0,00/)).toBeTruthy()
  })
})

describe('Button', () => {
  it('no dispara onPress cuando está deshabilitado', async () => {
    const onPress = jest.fn()
    await render(<Button label="Reservar" disabled onPress={onPress} />)

    await fireEvent.press(screen.getByText('Reservar'))

    expect(onPress).not.toHaveBeenCalled()
  })

  it('expone el estado deshabilitado a accesibilidad, no solo con opacidad', async () => {
    await render(<Button label="Reservar" disabled />)

    expect(screen.getByRole('button').props.accessibilityState).toMatchObject({ disabled: true })
  })

  it('mientras carga se bloquea y esconde el label', async () => {
    const onPress = jest.fn()
    await render(<Button label="Entrar" loading onPress={onPress} />)

    expect(screen.queryByText('Entrar')).toBeNull()
    expect(screen.getByRole('button').props.accessibilityState).toMatchObject({ disabled: true, busy: true })
  })
})

describe('TextField', () => {
  it('muestra el mensaje de error además del borde rojo', async () => {
    await render(<TextField label="Email" error="Ingresá un email válido" />)

    expect(screen.getByText('Ingresá un email válido')).toBeTruthy()
  })

  it('usa el label como etiqueta accesible del input', async () => {
    await render(<TextField label="Contraseña" />)

    expect(screen.getByLabelText('Contraseña')).toBeTruthy()
  })
})

describe('FormError', () => {
  it('no renderiza nada sin mensaje', async () => {
    const { toJSON } = await render(<FormError message={null} />)

    expect(toJSON()).toBeNull()
  })

  it('se anuncia como alerta cuando hay mensaje', async () => {
    await render(<FormError message="Credenciales inválidas" />)

    expect(screen.getByRole('alert')).toBeTruthy()
  })
})

describe('CatalogImage', () => {
  it('sin foto muestra el fallback con el nombre del lugar, no un hueco', async () => {
    await render(<CatalogImage uri={null} fallbackLabel="Uyuni" />)

    expect(screen.getByText('Uyuni')).toBeTruthy()
    expect(screen.queryByTestId('catalog-image')).toBeNull()
  })

  it('intenta la foto cuando hay URL', async () => {
    await render(<CatalogImage uri="https://cdn.example.com/foto.jpg" fallbackLabel="Uyuni" />)

    expect(screen.queryByText('Uyuni')).toBeNull()
    expect(screen.getByTestId('catalog-image')).toBeTruthy()
  })

  /**
   * Encontrado ejecutando la app: las fotos sembradas apuntan a example.com y no cargan, y sin este
   * fallback queda un hueco en blanco del alto de la tarjeta, que se lee como un error de la app.
   */
  it('cae al fallback si la foto existe pero no carga', async () => {
    await render(<CatalogImage uri="https://example.com/rota.jpg" fallbackLabel="Uyuni" />)

    await fireEvent(screen.getByTestId('catalog-image'), 'error', { nativeEvent: { error: 'no se pudo cargar' } })

    expect(screen.getByText('Uyuni')).toBeTruthy()
  })

  /**
   * Encontrado probando en un iPhone: NativeWind no traduce `className` en el Image de expo-image (no es un
   * componente de React Native), así que con el tamaño puesto por clase la foto se dibujaba de 0×0 en iOS y
   * Android. El tamaño tiene que ir en un contenedor y la foto llenarlo con un `style` explícito.
   */
  it('la foto no depende de className para tener tamaño en iOS y Android', async () => {
    await render(<CatalogImage uri="https://cdn.example.com/foto.jpg" className="h-44 w-full rounded-md" />)

    const image = screen.getByTestId('catalog-image')
    expect(image.props.className).toBeUndefined()
    expect(StyleSheet.flatten(image.props.style)).toMatchObject({
      position: 'absolute',
      top: 0,
      right: 0,
      bottom: 0,
      left: 0,
    })

    // Las clases de tamaño las recibe el contenedor, que además recorta las esquinas redondeadas.
    const container = screen.getByTestId('catalog-image-container')
    expect(container.props.className).toContain('h-44 w-full rounded-md')
    expect(container.props.className).toContain('overflow-hidden')
  })

  it('vuelve a intentar si el producto cambia de foto', async () => {
    const { rerender } = await render(<CatalogImage uri="https://example.com/rota.jpg" fallbackLabel="Uyuni" />)
    await fireEvent(screen.getByTestId('catalog-image'), 'error', { nativeEvent: { error: 'no se pudo cargar' } })
    expect(screen.getByText('Uyuni')).toBeTruthy()

    await rerender(<CatalogImage uri="https://cdn.example.com/nueva.jpg" fallbackLabel="Uyuni" />)

    expect(screen.queryByText('Uyuni')).toBeNull()
  })
})

describe('SegmentedControl', () => {
  const options = [
    { value: 'experiences' as const, label: 'Experiencias' },
    { value: 'packages' as const, label: 'Paquetes' },
  ]

  it('marca la opción activa para accesibilidad, no solo con color', async () => {
    await render(<SegmentedControl options={options} value="packages" onChange={jest.fn()} />)

    const tabs = screen.getAllByRole('tab')
    expect(tabs[0].props.accessibilityState).toMatchObject({ selected: false })
    expect(tabs[1].props.accessibilityState).toMatchObject({ selected: true })
  })

  it('avisa del cambio con el valor, no con el índice', async () => {
    const onChange = jest.fn()
    await render(<SegmentedControl options={options} value="experiences" onChange={onChange} />)

    await fireEvent.press(screen.getByText('Paquetes'))

    expect(onChange).toHaveBeenCalledWith('packages')
  })
})

describe('Badge', () => {
  it('no ocupa lugar cuando no hay filtros puestos', async () => {
    const { toJSON } = await render(<Badge count={0} />)

    expect(toJSON()).toBeNull()
  })

  it('muestra la cantidad', async () => {
    await render(<Badge count={3} />)

    expect(screen.getByText('3')).toBeTruthy()
  })
})
