namespace TurisClick.Api.Shared.Validation;

/// <summary>
/// Límites de las reglas de validación de la fase 1 (docs/auditoria-validaciones.md, §7.1).
///
/// Los mismos valores viven en <c>frontend/packages/validation/src/limits.ts</c>; el archivo común de casos
/// (<c>tests/contract/validation-vectors.json</c>) verifica que ambas implementaciones no diverjan.
/// </summary>
public static class ValidationLimits
{
    public const int OrganizationNameMin = 3;
    public const int OrganizationNameMax = 100;
    public const int CompanyDescriptionMax = 1000;

    public const int NitMinDigits = 5;
    public const int NitMaxDigits = 12;

    public const int EmailMax = 254;
    public const int EmailLocalPartMax = 64;
    public const int EmailDomainLabelMax = 63;

    public const int PersonNameMin = 2;
    public const int PersonNameMax = 100;

    public const int CategoryNameMin = 3;
    public const int CategoryNameMax = 40;
    public const int CategoryDescriptionMax = 300;

    public const int RejectionReasonMin = 5;
    public const int RejectionReasonMax = 500;

    public const int FlightSellerRegistrationMin = 3;
    public const int FlightSellerRegistrationMax = 100;

    public const int PasswordMin = 10;
    public const int PasswordMax = 200;

    /// <summary>Más repeticiones seguidas de un mismo carácter no numérico se consideran texto sin sentido.</summary>
    public const int MaxRepeatedChars = 3;
}
