using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Shared.Validation;

/// <summary>
/// Valida que un string sea un código de moneda ISO 4217 conocido. Lista curada de las monedas más
/// usadas (no las ~180 activas del estándar completo) — docs/backend-architecture.md ya documenta que
/// la lista definitiva vive en código, no en una tabla de referencia (no hay conversión/FX todavía).
/// Ampliable sin tocar la base de datos: la columna solo exige `CHECK (currency ~ '^[A-Z]{3}$')`.
/// </summary>
public class Iso4217CurrencyAttribute : ValidationAttribute
{
    private static readonly HashSet<string> KnownCurrencies =
    [
        "USD", "EUR", "GBP", "BOB", "ARS", "PEN", "CLP", "COP", "BRL", "UYU",
        "PYG", "MXN", "CAD", "AUD", "JPY", "CNY", "CHF", "SEK", "NOK", "DKK"
    ];

    public override bool IsValid(object? value)
    {
        if (value is not string currency)
            return false;

        return KnownCurrencies.Contains(currency);
    }

    public override string FormatErrorMessage(string name) =>
        $"El campo {name} debe ser un código de moneda ISO 4217 soportado (ej. USD, BOB, EUR).";
}
