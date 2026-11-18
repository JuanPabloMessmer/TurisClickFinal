using System.Text.Json;
using TurisClick.Api.Infrastructure.Database.Migrations;
using TurisClick.Api.Shared.Validation;
using Xunit;

namespace TurisClick.Api.Tests.Shared.Validation;

/// <summary>
/// Reglas de la fase 1 contra el archivo común de casos (tests/contract/validation-vectors.json), el mismo que
/// usan las pruebas del paquete de frontend <c>@turisclick/validation</c>. Si una de las dos implementaciones
/// cambia una regla sin la otra, falla una de las dos suites (docs/auditoria-validaciones.md, §11.1).
/// </summary>
public class ValidationVectorsTests
{
    private static readonly JsonElement Vectors = Load();

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "contract", "validation-vectors.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static IEnumerable<JsonElement> Cases(string name) => Vectors.GetProperty(name).EnumerateArray();

    private static string Describe(JsonElement c) => c.GetProperty("input").GetString() ?? "<null>";

    [Fact]
    public void LosLimitesCoincidenConLosDelFrontend()
    {
        var limits = Vectors.GetProperty("limits");
        var expected = new Dictionary<string, int>
        {
            ["organizationNameMin"] = ValidationLimits.OrganizationNameMin,
            ["organizationNameMax"] = ValidationLimits.OrganizationNameMax,
            ["companyDescriptionMax"] = ValidationLimits.CompanyDescriptionMax,
            ["nitMinDigits"] = ValidationLimits.NitMinDigits,
            ["nitMaxDigits"] = ValidationLimits.NitMaxDigits,
            ["emailMax"] = ValidationLimits.EmailMax,
            ["emailLocalPartMax"] = ValidationLimits.EmailLocalPartMax,
            ["personNameMin"] = ValidationLimits.PersonNameMin,
            ["personNameMax"] = ValidationLimits.PersonNameMax,
            ["categoryNameMin"] = ValidationLimits.CategoryNameMin,
            ["categoryNameMax"] = ValidationLimits.CategoryNameMax,
            ["categoryDescriptionMax"] = ValidationLimits.CategoryDescriptionMax,
            ["rejectionReasonMin"] = ValidationLimits.RejectionReasonMin,
            ["rejectionReasonMax"] = ValidationLimits.RejectionReasonMax,
            ["flightSellerRegistrationMin"] = ValidationLimits.FlightSellerRegistrationMin,
            ["flightSellerRegistrationMax"] = ValidationLimits.FlightSellerRegistrationMax,
            ["passwordMin"] = ValidationLimits.PasswordMin,
            ["passwordMax"] = ValidationLimits.PasswordMax,
            ["maxRepeatedChars"] = ValidationLimits.MaxRepeatedChars,
        };
        foreach (var (name, value) in expected)
            Assert.True(limits.GetProperty(name).GetInt32() == value, $"{name}: archivo común {limits.GetProperty(name)} ≠ C# {value}");
    }

    [Fact]
    public void TextoDeUnaLinea()
    {
        foreach (var c in Cases("singleLine"))
            Assert.True(TextNormalizer.SingleLine(c.GetProperty("input").GetString()) == c.GetProperty("expected").GetString(), Describe(c));
    }

    [Fact]
    public void Nit()
    {
        foreach (var c in Cases("nit"))
        {
            var normalized = FieldRules.Nit.Normalize(c.GetProperty("input").GetString());
            Assert.True(normalized == c.GetProperty("normalized").GetString(), $"normalización de «{Describe(c)}»: {normalized}");
            var valid = FieldRules.Nit.Validate(normalized) is null;
            Assert.True(valid == c.GetProperty("valid").GetBoolean(), $"«{Describe(c)}»: válido={valid}");
        }
    }

    [Fact]
    public void Telefono()
    {
        foreach (var c in Cases("phone"))
        {
            var (value, error) = FieldRules.Phone.Parse(c.GetProperty("input").GetString());
            var expected = c.GetProperty("normalized");
            Assert.True(value == (expected.ValueKind == JsonValueKind.Null ? null : expected.GetString()), $"«{Describe(c)}»: {value}");
            Assert.True((error is null) == c.GetProperty("valid").GetBoolean(), $"«{Describe(c)}»: {error}");
        }
    }

    [Fact]
    public void Correo()
    {
        foreach (var c in Cases("email"))
        {
            var normalized = FieldRules.Email.Normalize(c.GetProperty("input").GetString());
            Assert.True(normalized == c.GetProperty("normalized").GetString(), $"normalización de «{Describe(c)}»");
            var error = FieldRules.Email.Validate(normalized);
            Assert.True((error is null) == c.GetProperty("valid").GetBoolean(), $"«{Describe(c)}»: {error}");
        }
    }

    [Theory]
    [InlineData("organizationName")]
    [InlineData("personName")]
    [InlineData("categoryName")]
    public void Nombres(string rule)
    {
        foreach (var c in Cases(rule))
        {
            var value = TextNormalizer.SingleLine(c.GetProperty("input").GetString());
            var error = rule switch
            {
                "organizationName" => FieldRules.OrganizationName(value),
                "personName" => FieldRules.PersonName(value, "El nombre"),
                _ => FieldRules.CategoryName(value),
            };
            Assert.True((error is null) == c.GetProperty("valid").GetBoolean(), $"{rule} «{Describe(c)}»: {error}");
        }
    }

    [Fact]
    public void ClaveDeUnicidad()
    {
        foreach (var c in Cases("nameKey"))
            Assert.True(TextNormalizer.NameKey(c.GetProperty("input").GetString()!) == c.GetProperty("key").GetString(), Describe(c));
    }

    [Fact]
    public void ClaveDeSimilitud()
    {
        foreach (var c in Cases("similarityKey"))
            Assert.True(TextNormalizer.SimilarityKey(c.GetProperty("input").GetString()!) == c.GetProperty("key").GetString(), Describe(c));
    }

    [Fact]
    public void LaMigracionUsaLasMismasExpresionesQueElCodigo()
    {
        // Los índices de la 0016 y las consultas de los servicios tienen que calcular la misma clave.
        Assert.Equal(NameKeySql.NameKey("name"), _0016_Fase1_Validaciones.NameKeyExpression);
        Assert.Equal(NameKeySql.NormalizedNit("legal_document"), _0016_Fase1_Validaciones.NormalizedNitExpression);
    }

    [Fact]
    public void LosMensajesDeErrorEstanEnEspanolYNombranElCampo()
    {
        Assert.Equal("El NIT debe tener entre 5 y 12 dígitos (tiene 4).", FieldRules.Nit.Validate("1234"));
        Assert.Equal("El NIT sólo puede tener números.", FieldRules.Nit.Validate("12A45"));
        Assert.Equal("El teléfono no puede tener letras.", FieldRules.Phone.Parse("7ABC4567").Error);
        Assert.Equal("El correo de contacto no puede tener espacios.", FieldRules.Email.Validate("a b@x.bo", "El correo de contacto"));
        Assert.Equal("El nombre de la empresa debe incluir al menos dos letras.", FieldRules.OrganizationName("123"));
        Assert.Equal("El nombre de la empresa tiene un carácter no permitido: «<».", FieldRules.OrganizationName("Andes <b>"));
    }
}
