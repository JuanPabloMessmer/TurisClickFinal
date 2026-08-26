import type { components } from './generated/schema'

/**
 * Alias de conveniencia sobre los tipos generados desde /swagger/v1/swagger.json (`npm run generate:types`).
 * No se declaran a mano para no desincronizarse de los DTOs reales del backend.
 */
export type Schemas = components['schemas']

export type AuthResultResponse = Schemas['AuthResultResponse']
export type UserSummaryResponse = Schemas['UserSummaryResponse']
export type LoginRequest = Schemas['LoginRequest']
export type RefreshTokenRequest = Schemas['RefreshTokenRequest']
export type LogoutRequest = Schemas['LogoutRequest']

export type DestinationResponse = Schemas['DestinationResponse']
export type PublicDestinationResponse = Schemas['PublicDestinationResponse']
export type CreateDestinationRequest = Schemas['CreateDestinationRequest']
export type UpdateDestinationRequest = Schemas['UpdateDestinationRequest']

export type CategoryResponse = Schemas['CategoryResponse']
export type CreateCategoryRequest = Schemas['CreateCategoryRequest']
export type UpdateCategoryRequest = Schemas['UpdateCategoryRequest']

export type CompanyResponse = Schemas['CompanyResponse']
export type CompanyResponsePagedResult = Schemas['CompanyResponsePagedResult']
export type RejectCompanyRequest = Schemas['RejectCompanyRequest']
export type UpdateCompanyRequest = Schemas['UpdateCompanyRequest']
export type RegisterProviderRequest = Schemas['RegisterProviderRequest']
export type RegisterProviderResponse = Schemas['RegisterProviderResponse']

export type ExperienceResponse = Schemas['ExperienceResponse']
export type ExperienceSummaryResponse = Schemas['ExperienceSummaryResponse']
export type ExperienceSummaryResponsePagedResult = Schemas['ExperienceSummaryResponsePagedResult']
export type CreateExperienceRequest = Schemas['CreateExperienceRequest']
export type UpdateExperienceRequest = Schemas['UpdateExperienceRequest']
export type ExperienceImageRequest = Schemas['ExperienceImageRequest']

export type ExperienceAvailabilityResponse = Schemas['ExperienceAvailabilityResponse']
export type CreateExperienceAvailabilityRequest = Schemas['CreateExperienceAvailabilityRequest']

export type ReservationItemResponse = Schemas['ReservationItemResponse']
export type ReservationItemResponsePagedResult = Schemas['ReservationItemResponsePagedResult']

/** Valores reales devueltos por el backend (DTOs los exponen como string, no como enum numérico). */
export const DestinationTypes = ['COUNTRY', 'REGION', 'CITY'] as const
export type DestinationTypeValue = (typeof DestinationTypes)[number]

export const CompanyStatuses = ['PENDING_APPROVAL', 'APPROVED', 'REJECTED', 'SUSPENDED'] as const
export type CompanyStatusValue = (typeof CompanyStatuses)[number]

export const PublicationStatuses = ['DRAFT', 'PUBLISHED', 'UNPUBLISHED'] as const
export type PublicationStatusValue = (typeof PublicationStatuses)[number]
