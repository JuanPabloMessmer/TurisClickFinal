import type { components } from './generated/schema'

/**
 * Alias de conveniencia sobre los tipos generados desde /swagger/v1/swagger.json (`npm run generate:types`).
 * No se declaran a mano para no desincronizarse de los DTOs reales del backend.
 */
export type Schemas = components['schemas']

export type AuthResultResponse = Schemas['AuthResultResponse']
export type UserSummaryResponse = Schemas['UserSummaryResponse']
export type LoginRequest = Schemas['LoginRequest']
export type RegisterTouristRequest = Schemas['RegisterTouristRequest']
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
export type BulkCreateExperienceAvailabilityRequest = Schemas['BulkCreateExperienceAvailabilityRequest']
export type BulkExperienceAvailabilityResponse = Schemas['BulkExperienceAvailabilityResponse']
export type SkippedAvailabilityResponse = Schemas['SkippedAvailabilityResponse']
export type UpdateAvailabilityRequest = Schemas['UpdateAvailabilityRequest']

export type PackageResponse = Schemas['PackageResponse']
export type PackageSummaryResponse = Schemas['PackageSummaryResponse']
export type PackageSummaryResponsePagedResult = Schemas['PackageSummaryResponsePagedResult']
export type CreatePackageRequest = Schemas['CreatePackageRequest']
export type UpdatePackageRequest = Schemas['UpdatePackageRequest']
export type PackageItemRequest = Schemas['PackageItemRequest']
export type PackageItemResponse = Schemas['PackageItemResponse']
export type PackageImageRequest = Schemas['PackageImageRequest']
export type PackageImageResponse = Schemas['PackageImageResponse']

export type PackageAvailabilityResponse = Schemas['PackageAvailabilityResponse']
export type CreatePackageAvailabilityRequest = Schemas['CreatePackageAvailabilityRequest']
export type BulkCreatePackageAvailabilityRequest = Schemas['BulkCreatePackageAvailabilityRequest']
export type BulkPackageAvailabilityResponse = Schemas['BulkPackageAvailabilityResponse']
export type SkippedDepartureResponse = Schemas['SkippedDepartureResponse']

/** Presets del calendario del proveedor (Shared/Scheduling/AvailabilitySchedule en el backend). */
export const AvailabilityPresets = ['EVERY_DAY', 'WEEKDAYS', 'WEEKENDS', 'CUSTOM'] as const
export type AvailabilityPresetValue = (typeof AvailabilityPresets)[number]

export type ReservationItemResponse = Schemas['ReservationItemResponse']
export type ReservationItemResponsePagedResult = Schemas['ReservationItemResponsePagedResult']

export type CreateReservationRequest = Schemas['CreateReservationRequest']
export type PayReservationRequest = Schemas['PayReservationRequest']
export type ReservationResponse = Schemas['ReservationResponse']
export type ReservationResponsePagedResult = Schemas['ReservationResponsePagedResult']
export type ReservationTotalResponse = Schemas['ReservationTotalResponse']

/**
 * Valores reales de `ReservationResponse.status`. `PAYMENT_FAILED` existe en el enum del backend pero hoy
 * ningún código lo escribe: un pago rechazado deja la reserva en `PENDING_PAYMENT`.
 */
export const ReservationStatuses = ['PENDING_PAYMENT', 'CONFIRMED', 'PAYMENT_FAILED', 'CANCELLED', 'EXPIRED'] as const
export type ReservationStatusValue = (typeof ReservationStatuses)[number]

export const ReservationItemStatuses = ['PENDING_PAYMENT', 'CONFIRMED', 'CANCELLED', 'EXPIRED'] as const
export type ReservationItemStatusValue = (typeof ReservationItemStatuses)[number]

/** Valores reales devueltos por el backend (DTOs los exponen como string, no como enum numérico). */
export const DestinationTypes = ['COUNTRY', 'REGION', 'CITY'] as const
export type DestinationTypeValue = (typeof DestinationTypes)[number]

export const CompanyStatuses = ['PENDING_APPROVAL', 'APPROVED', 'REJECTED', 'SUSPENDED'] as const
export type CompanyStatusValue = (typeof CompanyStatuses)[number]

export const PublicationStatuses = ['DRAFT', 'PUBLISHED', 'UNPUBLISHED'] as const
export type PublicationStatusValue = (typeof PublicationStatuses)[number]
