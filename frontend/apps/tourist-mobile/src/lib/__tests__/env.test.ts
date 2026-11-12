import { AZURE_API_BASE_URL, resolveApiConfig } from '@/lib/env'

const inferLocal = () => 'http://192.168.0.10:5288'

describe('resolveApiConfig', () => {
  it('usa Azure por defecto, sin ninguna variable definida', async () => {
    expect(resolveApiConfig({}, inferLocal)).toEqual({ target: 'azure', baseUrl: AZURE_API_BASE_URL })
  })

  it('usa Azure con EXPO_PUBLIC_API_TARGET=azure y con valores desconocidos', async () => {
    expect(resolveApiConfig({ target: 'azure' }, inferLocal).baseUrl).toBe(AZURE_API_BASE_URL)
    expect(resolveApiConfig({ target: 'staging' }, inferLocal).baseUrl).toBe(AZURE_API_BASE_URL)
  })

  it('con EXPO_PUBLIC_API_TARGET=local deduce la URL del backend local', async () => {
    expect(resolveApiConfig({ target: ' LOCAL ' }, inferLocal)).toEqual({
      target: 'local',
      baseUrl: 'http://192.168.0.10:5288',
    })
  })

  it('EXPO_PUBLIC_API_BASE_URL pisa al target y se normaliza sin barra final', async () => {
    expect(resolveApiConfig({ target: 'azure', baseUrl: 'http://10.0.2.2:5288/' }, inferLocal)).toEqual({
      target: 'custom',
      baseUrl: 'http://10.0.2.2:5288',
    })
  })

  it('ignora una EXPO_PUBLIC_API_BASE_URL vacía', async () => {
    expect(resolveApiConfig({ baseUrl: '   ' }, inferLocal).target).toBe('azure')
  })

  it('la URL de Azure es HTTPS', async () => {
    expect(AZURE_API_BASE_URL.startsWith('https://')).toBe(true)
  })
})
