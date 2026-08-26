import { isAxiosError } from 'axios'

/**
 * El backend responde errores de negocio como ProblemDetails ({ title, detail, status }) vía
 * GlobalExceptionHandler. Los 400 de validación de modelo (DataAnnotations en el DTO, antes de llegar
 * al Service) usan el shape estándar de ASP.NET Core ([ApiController]): ValidationProblemDetails con un
 * diccionario `errors`.
 */
export function getErrorMessage(error: unknown): string {
  if (isAxiosError(error)) {
    const data = error.response?.data as
      | { detail?: string; title?: string; errors?: Record<string, string[]> }
      | undefined
    if (data?.errors) return Object.values(data.errors).flat().join(' ')
    return data?.detail ?? data?.title ?? error.message
  }
  if (error instanceof Error) return error.message
  return 'Ocurrió un error inesperado.'
}
