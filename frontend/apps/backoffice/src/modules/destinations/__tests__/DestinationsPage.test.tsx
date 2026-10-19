import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { destinationsApi } from '@turisclick/api-client'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DestinationsPage } from '@/modules/destinations/DestinationsPage'
import { IMAGE_MESSAGES } from '@/modules/destinations/imageFile'

/**
 * UC-A-04 — imagen del destino subida desde la computadora. Lo que se fija: elegir un archivo no sube nada
 * (sólo previsualiza), la subida ocurre al guardar y su URL viaja como `imageUrl` en el alta o la edición, un
 * archivo inválido se frena antes de llamar al servidor, y un doble clic no sube ni guarda dos veces.
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    destinationsApi: {
      ...actual.destinationsApi,
      listDestinations: vi.fn(),
      createDestination: vi.fn(),
      updateDestination: vi.fn(),
      uploadDestinationImage: vi.fn(),
    },
  }
})

const listDestinations = vi.mocked(destinationsApi.listDestinations)
const createDestination = vi.mocked(destinationsApi.createDestination)
const updateDestination = vi.mocked(destinationsApi.updateDestination)
const uploadDestinationImage = vi.mocked(destinationsApi.uploadDestinationImage)

const WIKIMEDIA = 'https://upload.wikimedia.org/wikipedia/commons/a/aa/Uyuni.jpg'
const UPLOADED = 'https://stturisclickv2imgjpm.blob.core.windows.net/images/destinations/2026/10/abc.webp'
const BOLIVIA = { id: 'bo', name: 'Bolivia', type: 'COUNTRY', parentId: null, parentName: null, imageUrl: WIKIMEDIA }

beforeEach(() => {
  listDestinations.mockResolvedValue([BOLIVIA])
  // jsdom no implementa las URLs blob: para la vista previa.
  URL.createObjectURL = vi.fn(() => 'blob:preview')
  URL.revokeObjectURL = vi.fn()
})

afterEach(() => {
  vi.clearAllMocks()
})

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <DestinationsPage />
    </QueryClientProvider>,
  )
}

function image(name = 'uyuni.webp', type = 'image/webp', size = 2048) {
  return new File([new Uint8Array(size)], name, { type })
}

function chooseFile(dialog: HTMLElement, file: File) {
  fireEvent.change(within(dialog).getByLabelText('Imagen representativa (opcional)'), { target: { files: [file] } })
}

async function openCreate() {
  renderPage()
  fireEvent.click(await screen.findByRole('button', { name: /Crear destino/ }))
  return screen.getByRole('dialog')
}

async function openEdit() {
  renderPage()
  fireEvent.click(await screen.findByRole('button', { name: /Editar/ }))
  return screen.getByRole('dialog')
}

describe('DestinationsPage — imagen al crear', () => {
  it('ya no pide una URL: ofrece elegir un archivo', async () => {
    const dialog = await openCreate()

    expect(within(dialog).queryByPlaceholderText(/https:/)).toBeNull()
    expect(within(dialog).getByRole('button', { name: /Seleccionar imagen/ })).toBeTruthy()
    expect(within(dialog).getByText('JPG, PNG o WebP. Máximo 5 MB.')).toBeTruthy()
  })

  it('previsualiza sin subir, sube al guardar y crea el destino con la URL devuelta', async () => {
    uploadDestinationImage.mockImplementation(async (_http, _file, onProgress) => {
      onProgress?.(100)
      return { url: UPLOADED }
    })
    createDestination.mockResolvedValue({ id: 'pe', name: 'Perú', type: 'COUNTRY', imageUrl: UPLOADED })
    const dialog = await openCreate()
    const file = image()

    fireEvent.change(within(dialog).getByLabelText('Nombre'), { target: { value: 'Perú' } })
    chooseFile(dialog, file)

    expect((within(dialog).getByAltText('Vista previa de la imagen elegida') as HTMLImageElement).src).toBe('blob:preview')
    expect(within(dialog).getByText(/uyuni\.webp · 2 KB\. Se sube al guardar\./)).toBeTruthy()
    expect(uploadDestinationImage).not.toHaveBeenCalled()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear' }))

    await waitFor(() => expect(createDestination).toHaveBeenCalled())
    expect(uploadDestinationImage).toHaveBeenCalledWith(expect.anything(), file, expect.any(Function))
    expect(createDestination.mock.calls[0][1]).toEqual({ name: 'Perú', type: 'COUNTRY', parentId: undefined, imageUrl: UPLOADED })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('sin imagen crea el destino sin imageUrl y sin subir nada', async () => {
    createDestination.mockResolvedValue({ id: 'pe', name: 'Perú', type: 'COUNTRY' })
    const dialog = await openCreate()

    fireEvent.change(within(dialog).getByLabelText('Nombre'), { target: { value: 'Perú' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear' }))

    await waitFor(() => expect(createDestination).toHaveBeenCalled())
    expect(createDestination.mock.calls[0][1]).toMatchObject({ imageUrl: undefined })
    expect(uploadDestinationImage).not.toHaveBeenCalled()
  })

  it.each([
    [image('animada.gif', 'image/gif'), IMAGE_MESSAGES.unsupported],
    [image('enorme.jpg', 'image/jpeg', 5 * 1024 * 1024 + 1), IMAGE_MESSAGES.tooLarge],
  ])('frena un archivo inválido antes de subirlo (%#)', async (file, message) => {
    const dialog = await openCreate()

    chooseFile(dialog, file)

    expect(within(dialog).getByRole('alert').textContent).toBe(message)
    expect(within(dialog).queryByAltText('Vista previa de la imagen elegida')).toBeNull()
    expect(uploadDestinationImage).not.toHaveBeenCalled()
  })

  it('muestra el progreso y un doble clic no sube ni crea dos veces', async () => {
    let finishUpload: (value: { url: string }) => void = () => {}
    uploadDestinationImage.mockImplementation((_http, _file, onProgress) => {
      onProgress?.(40)
      return new Promise((resolve) => {
        finishUpload = resolve
      })
    })
    createDestination.mockResolvedValue({ id: 'pe', name: 'Perú', type: 'COUNTRY', imageUrl: UPLOADED })
    const dialog = await openCreate()

    fireEvent.change(within(dialog).getByLabelText('Nombre'), { target: { value: 'Perú' } })
    chooseFile(dialog, image())
    const submit = within(dialog).getByRole('button', { name: 'Crear' })
    fireEvent.click(submit)
    fireEvent.click(submit)

    expect(await within(dialog).findByText('Subiendo imagen… 40%')).toBeTruthy()
    expect(within(dialog).getByRole('progressbar').getAttribute('aria-valuenow')).toBe('40')
    expect((within(dialog).getByRole('button', { name: /Subiendo imagen/ }) as HTMLButtonElement).disabled).toBe(true)
    expect((within(dialog).getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement).disabled).toBe(true)

    await act(async () => finishUpload({ url: UPLOADED }))

    await waitFor(() => expect(createDestination).toHaveBeenCalledTimes(1))
    expect(uploadDestinationImage).toHaveBeenCalledTimes(1)
  })

  it('si la subida falla muestra el motivo y no crea el destino', async () => {
    uploadDestinationImage.mockRejectedValue(
      new AxiosError('fallo', 'ERR_BAD_RESPONSE', undefined, undefined, {
        status: 503,
        statusText: 'Service Unavailable',
        headers: {},
        config: { headers: new AxiosHeaders() },
        data: { detail: 'No se pudo guardar la imagen en este momento. Intentá de nuevo en unos minutos.' },
      }),
    )
    const dialog = await openCreate()

    fireEvent.change(within(dialog).getByLabelText('Nombre'), { target: { value: 'Perú' } })
    chooseFile(dialog, image())
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear' }))

    expect(await within(dialog).findByText('No se pudo guardar la imagen en este momento. Intentá de nuevo en unos minutos.')).toBeTruthy()
    expect(createDestination).not.toHaveBeenCalled()
  })

  it('si falla el alta, reintentar con el mismo archivo no lo vuelve a subir', async () => {
    uploadDestinationImage.mockResolvedValue({ url: UPLOADED })
    createDestination
      .mockRejectedValueOnce(
        new AxiosError('conflicto', 'ERR_BAD_REQUEST', undefined, undefined, {
          status: 409,
          statusText: 'Conflict',
          headers: {},
          config: { headers: new AxiosHeaders() },
          data: { detail: 'Ya existe un destino con ese nombre en el mismo nivel.' },
        }),
      )
      .mockResolvedValueOnce({ id: 'pe', name: 'Perú 2', type: 'COUNTRY', imageUrl: UPLOADED })
    const dialog = await openCreate()

    fireEvent.change(within(dialog).getByLabelText('Nombre'), { target: { value: 'Bolivia' } })
    chooseFile(dialog, image())
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear' }))
    expect(await within(dialog).findByText('Ya existe un destino con ese nombre en el mismo nivel.')).toBeTruthy()

    fireEvent.change(within(dialog).getByLabelText('Nombre'), { target: { value: 'Perú 2' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear' }))

    await waitFor(() => expect(createDestination).toHaveBeenCalledTimes(2))
    expect(uploadDestinationImage).toHaveBeenCalledTimes(1)
    expect(createDestination.mock.calls[1][1]).toMatchObject({ imageUrl: UPLOADED })
  })
})

describe('DestinationsPage — imagen al editar', () => {
  it('muestra la imagen actual y renombrar la conserva (URL externa previa incluida)', async () => {
    updateDestination.mockResolvedValue({ ...BOLIVIA, name: 'Estado Plurinacional de Bolivia' })
    const dialog = await openEdit()

    expect((within(dialog).getByAltText('Imagen actual del destino') as HTMLImageElement).src).toBe(WIKIMEDIA)
    expect(within(dialog).getByRole('button', { name: /Reemplazar imagen/ })).toBeTruthy()

    fireEvent.change(within(dialog).getByLabelText('Nombre'), { target: { value: 'Estado Plurinacional de Bolivia' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }))

    await waitFor(() => expect(updateDestination).toHaveBeenCalled())
    expect(updateDestination.mock.calls[0].slice(1)).toEqual(['bo', { name: 'Estado Plurinacional de Bolivia', imageUrl: WIKIMEDIA }])
    expect(uploadDestinationImage).not.toHaveBeenCalled()
  })

  it('reemplaza la imagen con un archivo nuevo', async () => {
    uploadDestinationImage.mockResolvedValue({ url: UPLOADED })
    updateDestination.mockResolvedValue({ ...BOLIVIA, imageUrl: UPLOADED })
    const dialog = await openEdit()

    chooseFile(dialog, image('nueva.png', 'image/png'))
    expect(within(dialog).getByAltText('Vista previa de la imagen elegida')).toBeTruthy()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }))

    await waitFor(() => expect(updateDestination).toHaveBeenCalled())
    expect(updateDestination.mock.calls[0][2]).toEqual({ name: 'Bolivia', imageUrl: UPLOADED })
  })

  it('quitar la imagen manda imageUrl null, y "Mantener la actual" lo deshace', async () => {
    updateDestination.mockResolvedValue({ ...BOLIVIA, imageUrl: null })
    const dialog = await openEdit()

    fireEvent.click(within(dialog).getByRole('button', { name: /Quitar imagen/ }))
    expect(within(dialog).getByText('La imagen se quitará al guardar.')).toBeTruthy()

    fireEvent.click(within(dialog).getByRole('button', { name: /Mantener la actual/ }))
    expect(within(dialog).getByAltText('Imagen actual del destino')).toBeTruthy()

    fireEvent.click(within(dialog).getByRole('button', { name: /Quitar imagen/ }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }))

    await waitFor(() => expect(updateDestination).toHaveBeenCalled())
    expect(updateDestination.mock.calls[0][2]).toEqual({ name: 'Bolivia', imageUrl: null })
  })
})
