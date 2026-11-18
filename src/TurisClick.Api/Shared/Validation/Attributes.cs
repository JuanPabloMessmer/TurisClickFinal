using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Shared.Validation;

/// <summary>
/// Atributos de validación de la fase 1. Todos validan el valor **normalizado** con las reglas de
/// <see cref="FieldRules"/> y devuelven el mensaje en español, asociado a la propiedad. Los servicios guardan
/// ese mismo valor normalizado.
///
/// Se agrupan en una clase estática para que el uso sea explícito (<c>[Rule.OrganizationName]</c>).
/// </summary>
public static class Rule
{
    /// <summary>Base común: obtiene el texto, delega en la regla y arma el resultado.</summary>
    public abstract class TextRuleAttribute : ValidationAttribute
    {
        protected abstract string? Check(string? value);

        protected sealed override ValidationResult? IsValid(object? value, ValidationContext context)
        {
            if (value is not null and not string)
                return new ValidationResult("El valor de este campo no es válido.", MemberOf(context));
            var error = Check(value as string);
            return error is null ? ValidationResult.Success : new ValidationResult(error, MemberOf(context));
        }

        private static string[] MemberOf(ValidationContext context) =>
            context.MemberName is null ? [] : [context.MemberName];
    }

    /// <summary>R-01 · nombre de empresa (obligatorio).</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class OrganizationNameAttribute : TextRuleAttribute
    {
        protected override string? Check(string? value) => FieldRules.OrganizationName(TextNormalizer.SingleLine(value));
    }

    /// <summary>R-03 · NIT boliviano (obligatorio).</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class BolivianNitAttribute : TextRuleAttribute
    {
        protected override string? Check(string? value) => FieldRules.Nit.Validate(FieldRules.Nit.Normalize(value));
    }

    /// <summary>R-04 · correo (obligatorio). <see cref="Label"/> nombra el campo en el mensaje.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class StrictEmailAttribute : TextRuleAttribute
    {
        public string Label { get; init; } = "El correo";

        protected override string? Check(string? value) => FieldRules.Email.Validate(FieldRules.Email.Normalize(value), Label);
    }

    /// <summary>R-05 · teléfono de contacto (opcional).</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ContactPhoneAttribute : TextRuleAttribute
    {
        protected override string? Check(string? value) => FieldRules.Phone.Parse(value).Error;
    }

    /// <summary>R-06 · nombre o apellido de una persona (obligatorio).</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class PersonNameAttribute(string label) : TextRuleAttribute
    {
        protected override string? Check(string? value) => FieldRules.PersonName(TextNormalizer.SingleLine(value), label);
    }

    /// <summary>R-07 · nombre de categoría (obligatorio).</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class CategoryNameAttribute : TextRuleAttribute
    {
        protected override string? Check(string? value) => FieldRules.CategoryName(TextNormalizer.SingleLine(value));
    }

    /// <summary>Texto libre (descripciones, motivos) con límites y, si corresponde, varias líneas.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class PlainTextAttribute(string label, int max) : TextRuleAttribute
    {
        public int Min { get; init; }
        public bool Required { get; init; }
        public bool MultiLine { get; init; }

        protected override string? Check(string? value)
        {
            var normalized = MultiLine ? TextNormalizer.MultiLine(value) : TextNormalizer.SingleLine(value);
            return FieldRules.PlainText(normalized, label, max, Min, Required, MultiLine);
        }
    }

    /// <summary>R-11 · registro o licencia para vender vuelos (opcional; la obligatoriedad la decide el DTO).</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class FlightSellerRegistrationAttribute : TextRuleAttribute
    {
        protected override string? Check(string? value) =>
            FieldRules.FlightSellerRegistration(TextNormalizer.NullIfEmpty(TextNormalizer.SingleLine(value)));
    }
}
