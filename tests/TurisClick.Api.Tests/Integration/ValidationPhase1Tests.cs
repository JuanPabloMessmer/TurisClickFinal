using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Admin.Dtos;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Companies.Dtos;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Fase 1 de validaciones (docs/auditoria-validaciones.md, §11.2): empresas, cuentas, categorías y errores.
/// Todo por **llamada directa a la API** —sin el frontend— y, donde importa, contra la base sin pasar por la
/// API, para probar que las reglas se sostienen también ante clientes que no son el backoffice y ante altas
/// simultáneas.
/// </summary>
[Collection(ApiCollection.Name)]
public partial class ValidationPhase1Tests(TurisClickApiFactory factory)
{
    private readonly TurisClickApiFactory _factory = factory;

    /// <summary>Restos de los mensajes por defecto de ASP.NET / System.Text.Json que nunca deben llegar al cliente.</summary>
    [GeneratedRegex(@"\bThe\b|\bfield\b|\bmust be\b|\bis required\b|System\.|\$\.|LineNumber|One or more", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishOrTechnical();

    private static string Digits(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(_ => (char)('0' + Random.Shared.Next(10))));

    private static string UniqueLabel() => Digits(8);

    private async Task<HttpClient> AdminAsync()
    {
        var client = _factory.CreateClient();
        UseBearerToken(client, await LoginAsAdminAsync(client));
        return client;
    }

    private static object ProviderPayload(
        string? companyName = null, string? legalDocument = null, string? contactPhone = null,
        string? contactEmail = null, string? email = null, string firstName = "Camila", string lastName = "Aramayo") => new
    {
        companyName = companyName ?? $"Operador {UniqueLabel()}",
        companyDescription = "Operador turístico de prueba.",
        legalDocument = legalDocument ?? NewNit(),
        contactEmail = contactEmail ?? $"contacto.{Guid.NewGuid():N}@turisclick.dev",
        contactPhone,
        firstName,
        lastName,
        email = email ?? $"operador.{Guid.NewGuid():N}@turisclick.dev",
        approve = true,
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>El error está debajo del campo indicado, en español y sin texto técnico.</summary>
    private static async Task<string> AssertFieldErrorAsync(HttpResponseMessage response, HttpStatusCode status, string field)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Esperaba {status} y llegó {response.StatusCode}: {text}");
        Assert.DoesNotMatch(EnglishOrTechnical(), text);
        var body = JsonDocument.Parse(text).RootElement;
        var messages = body.TryGetProperty("errors", out var errors) && errors.TryGetProperty(field, out var found)
            ? found
            : default;
        Assert.True(messages.ValueKind == JsonValueKind.Array, $"No hay error para «{field}»: {text}");
        var message = messages[0].GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(message));
        return message;
    }

    private async Task<T> QueryAsync<T>(Func<TurisClickDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TurisClickDbContext>());
    }

    // ================================================================ alta de operador (R-01 a R-06)

    [Theory]
    [InlineData("companyName", "AB")]
    [InlineData("companyName", "123")]
    [InlineData("companyName", "!!!!")]
    [InlineData("companyName", "   ")]
    [InlineData("companyName", "Andes Tours <script>alert(1)</script>")]
    [InlineData("legalDocument", "NIT-DEMO-1020304")]
    [InlineData("legalDocument", "1234")]
    [InlineData("legalDocument", "1234567890123")]
    [InlineData("legalDocument", "11111")]
    [InlineData("contactPhone", "7ABC4567")]
    [InlineData("contactPhone", "12345678")]
    [InlineData("contactPhone", "0054 11 1234 5678")]
    [InlineData("contactEmail", "contacto con espacios@turisclick.dev")]
    [InlineData("contactEmail", "josé@turisclick.dev")]
    [InlineData("email", "sin-arroba.turisclick.dev")]
    [InlineData("firstName", "J")]
    [InlineData("lastName", "Pérez2")]
    public async Task AltaDeOperador_DatoInvalido_400EnEspanolDebajoDelCampo(string field, string value)
    {
        var admin = await AdminAsync();
        var payload = field switch
        {
            "companyName" => ProviderPayload(companyName: value),
            "legalDocument" => ProviderPayload(legalDocument: value),
            "contactPhone" => ProviderPayload(contactPhone: value),
            "contactEmail" => ProviderPayload(contactEmail: value),
            "email" => ProviderPayload(email: value),
            "firstName" => ProviderPayload(firstName: value),
            _ => ProviderPayload(lastName: value),
        };

        var response = await admin.PostAsJsonAsync("/api/admin/provider-accounts", payload);

        await AssertFieldErrorAsync(response, HttpStatusCode.BadRequest, field);
    }

    [Fact]
    public async Task AltaDeOperador_TextosExcesivamenteLargos_400()
    {
        var admin = await AdminAsync();

        var longName = await admin.PostAsJsonAsync("/api/admin/provider-accounts",
            ProviderPayload(companyName: "Empresa " + string.Concat(Enumerable.Repeat("Andes Tours ", 10))));
        var longEmail = await admin.PostAsJsonAsync("/api/admin/provider-accounts",
            ProviderPayload(contactEmail: new string('a', 65) + "@turisclick.dev"));
        var hugeEmail = await admin.PostAsJsonAsync("/api/admin/provider-accounts",
            ProviderPayload(email: "ana@" + string.Join('.', Enumerable.Repeat(new string('d', 60), 5)) + ".bo"));

        Assert.Contains("entre 3 y 100", await AssertFieldErrorAsync(longName, HttpStatusCode.BadRequest, "companyName"));
        await AssertFieldErrorAsync(longEmail, HttpStatusCode.BadRequest, "contactEmail");
        Assert.Contains("254", await AssertFieldErrorAsync(hugeEmail, HttpStatusCode.BadRequest, "email"));
    }

    [Fact]
    public async Task AltaDeOperador_GuardaLosDatosNormalizados()
    {
        var admin = await AdminAsync();
        var label = UniqueLabel();
        var nit = NewNit();
        var formattedNit = $"{nit[..4]}.{nit[4..8]}-{nit[8..]}";

        var response = await admin.PostAsJsonAsync("/api/admin/provider-accounts", ProviderPayload(
            companyName: $"  Ñandú   Expediciones {label} ", legalDocument: formattedNit, contactPhone: "+591 7 123-4567",
            contactEmail: $"  Contacto.{label}@TurisClick.dev ", firstName: " María  José "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ProviderAccountCreatedResponse>(JsonOptions);
        var company = await QueryAsync(db => db.Companies.AsNoTracking().SingleAsync(c => c.Id == created!.Company.Id));
        var user = await QueryAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == created!.UserId));

        Assert.Equal($"Ñandú Expediciones {label}", company.Name);
        Assert.Equal(nit, company.LegalDocument);
        Assert.Equal("+59171234567", company.ContactPhone);
        Assert.Equal($"contacto.{label}@turisclick.dev", company.ContactEmail);
        Assert.Equal("María José", user.FirstName);
    }

    [Theory]
    [InlineData("22123456", "+59122123456")]
    [InlineData("800 10 1234", "+591800101234")]
    [InlineData("+54 11 1234 5678", "+541112345678")]
    [InlineData("", null)]
    public async Task AltaDeOperador_TelefonosValidos(string input, string? stored)
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync("/api/admin/provider-accounts", ProviderPayload(contactPhone: input));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ProviderAccountCreatedResponse>(JsonOptions);
        var company = await QueryAsync(db => db.Companies.AsNoTracking().SingleAsync(c => c.Id == created!.Company.Id));
        Assert.Equal(stored, company.ContactPhone);
    }

    [Fact]
    public async Task AltaDeOperador_Duplicados_409EnElCampoQueChoca()
    {
        var admin = await AdminAsync();
        var label = UniqueLabel();
        var nit = NewNit();
        var email = $"operador.{Guid.NewGuid():N}@turisclick.dev";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/provider-accounts",
            ProviderPayload(companyName: $"Andes Tours {label}", legalDocument: nit, email: email))).StatusCode);

        var sameNameOtherCase = await admin.PostAsJsonAsync("/api/admin/provider-accounts",
            ProviderPayload(companyName: $"  ANDES   TÓURS {label} "));
        var sameNitWithSeparators = await admin.PostAsJsonAsync("/api/admin/provider-accounts",
            ProviderPayload(legalDocument: $"{nit[..6]}-{nit[6..]}"));
        var sameEmailUppercase = await admin.PostAsJsonAsync("/api/admin/provider-accounts",
            ProviderPayload(email: email.ToUpperInvariant()));

        Assert.Contains("Ya existe una empresa con ese nombre",
            await AssertFieldErrorAsync(sameNameOtherCase, HttpStatusCode.Conflict, "companyName"));
        Assert.Equal("Ya existe una empresa con ese NIT.",
            await AssertFieldErrorAsync(sameNitWithSeparators, HttpStatusCode.Conflict, "legalDocument"));
        Assert.Equal("Ya existe una cuenta con ese correo.",
            await AssertFieldErrorAsync(sameEmailUppercase, HttpStatusCode.Conflict, "email"));
    }

    [Fact]
    public async Task AltaDeOperador_NombreSoloParecido_SeAceptaYElChequeoLoAdvierte()
    {
        var admin = await AdminAsync();
        var label = UniqueLabel();
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/provider-accounts",
            ProviderPayload(companyName: $"Andes Tours {label} S.R.L."))).StatusCode);

        var check = await JsonAsync(await admin.GetAsync(
            $"/api/admin/companies/name-check?name={Uri.EscapeDataString($"Andes Tours {label} SRL")}"));
        Assert.Equal(JsonValueKind.Null, check.GetProperty("exactMatch").ValueKind);
        Assert.Contains(check.GetProperty("similar").EnumerateArray(),
            s => s.GetProperty("name").GetString() == $"Andes Tours {label} S.R.L.");

        // Sólo difiere en puntuación y forma societaria: no se bloquea (C-3).
        var similar = await admin.PostAsJsonAsync("/api/admin/provider-accounts",
            ProviderPayload(companyName: $"Andes Tours {label} SRL"));
        Assert.Equal(HttpStatusCode.Created, similar.StatusCode);

        var exact = await JsonAsync(await admin.GetAsync(
            $"/api/admin/companies/name-check?name={Uri.EscapeDataString($"andes tours {label} srl")}"));
        Assert.Equal($"Andes Tours {label} SRL", exact.GetProperty("exactMatch").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ChequeoDeNombre_EsSoloParaAdministradores()
    {
        var provider = _factory.CreateClient();
        var account = await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), "namecheck");
        UseBearerToken(provider, account.AccessToken);
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await provider.GetAsync("/api/admin/companies/name-check?name=Andes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/companies/name-check?name=Andes")).StatusCode);
    }

    [Fact]
    public async Task AltaDeOperador_DosAltasSimultaneasConElMismoNit_UnaGanaYLaOtraRecibe409()
    {
        var admin = await AdminAsync();
        var nit = NewNit();

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            admin.PostAsJsonAsync("/api/admin/provider-accounts", ProviderPayload(legalDocument: nit))));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        Assert.Equal(1, await QueryAsync(db => db.Companies.CountAsync(c => c.LegalDocument == nit)));
    }

    // ================================================================ alta de administrador

    [Theory]
    [InlineData("firstName", "1")]
    [InlineData("lastName", "<b>Admin</b>")]
    [InlineData("email", "admin con espacio@turisclick.dev")]
    public async Task AltaDeAdministrador_DatoInvalido_400EnEspanol(string field, string value)
    {
        var admin = await AdminAsync();
        var payload = new Dictionary<string, string>
        {
            ["firstName"] = "Ricardo",
            ["lastName"] = "Quispe",
            ["email"] = $"admin.{Guid.NewGuid():N}@turisclick.dev",
            [field] = value,
        };

        var response = await admin.PostAsJsonAsync("/api/admin/admin-accounts", payload);

        await AssertFieldErrorAsync(response, HttpStatusCode.BadRequest, field);
    }

    [Fact]
    public async Task AltaDeAdministrador_CorreoRepetidoConOtrasMayusculas_409()
    {
        var admin = await AdminAsync();
        var email = $"admin.{Guid.NewGuid():N}@turisclick.dev";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/admin-accounts",
            new { firstName = "Ana", lastName = "Rojas", email })).StatusCode);

        var duplicate = await admin.PostAsJsonAsync("/api/admin/admin-accounts",
            new { firstName = "Ana", lastName = "Rojas", email = $"  {email.ToUpperInvariant()} " });

        await AssertFieldErrorAsync(duplicate, HttpStatusCode.Conflict, "email");
    }

    // ================================================================ Mi empresa

    [Fact]
    public async Task MiEmpresa_ReglasDeNombreTelefonoYCorreo()
    {
        var provider = _factory.CreateClient();
        var account = await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), "miempresa");
        UseBearerToken(provider, account.AccessToken);

        var badPhone = await provider.PutAsJsonAsync("/api/companies/me",
            new { name = account.Company.Name, contactEmail = "contacto@turisclick.dev", contactPhone = "7ABC4567" });
        var badName = await provider.PutAsJsonAsync("/api/companies/me",
            new { name = "!!", contactEmail = "contacto@turisclick.dev" });
        var tooLongDescription = await provider.PutAsJsonAsync("/api/companies/me",
            new { name = account.Company.Name, contactEmail = "contacto@turisclick.dev", description = new string('a', 1001) });
        var ok = await provider.PutAsJsonAsync("/api/companies/me",
            new { name = $" {account.Company.Name} ", contactEmail = "Contacto@TurisClick.dev", contactPhone = "33 123 456" });

        Assert.Equal("El teléfono no puede tener letras.",
            await AssertFieldErrorAsync(badPhone, HttpStatusCode.BadRequest, "contactPhone"));
        await AssertFieldErrorAsync(badName, HttpStatusCode.BadRequest, "name");
        await AssertFieldErrorAsync(tooLongDescription, HttpStatusCode.BadRequest, "description");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var saved = await ok.Content.ReadFromJsonAsync<CompanyResponse>(JsonOptions);
        Assert.Equal(account.Company.Name, saved!.Name);
        Assert.Equal("contacto@turisclick.dev", saved.ContactEmail);
        Assert.Equal("+59133123456", saved.ContactPhone);
    }

    [Fact]
    public async Task MiEmpresa_NoPuedeTomarElNombreDeOtraEmpresa()
    {
        var other = await RegisterApprovedProviderAsync(_factory.CreateClient(), _factory.CreateClient(), "otraempresa");
        var provider = _factory.CreateClient();
        var account = await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), "miempresa2");
        UseBearerToken(provider, account.AccessToken);

        var response = await provider.PutAsJsonAsync("/api/companies/me",
            new { name = other.Company.Name.ToUpperInvariant(), contactEmail = "contacto@turisclick.dev" });

        await AssertFieldErrorAsync(response, HttpStatusCode.Conflict, "name");
    }

    [Fact]
    public async Task DatosViejosInvalidos_NoBloqueanEditarNiAprobarNiSuspender()
    {
        // Una empresa cargada antes de la fase 1 con un NIT que hoy sería inválido (como los NIT-DEMO-… del
        // catálogo de demo): el NIT no se edita desde Mi empresa, y aprobar o suspender no lo revalida.
        var provider = _factory.CreateClient();
        var account = await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), "legado");
        UseBearerToken(provider, account.AccessToken);
        var legacyNit = $"NIT-LEGADO-{Guid.NewGuid():N}";
        await QueryAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE companies SET legal_document = {0} WHERE id = {1}", legacyNit, account.Company.Id));

        var edit = await provider.PutAsJsonAsync("/api/companies/me",
            new { name = account.Company.Name, contactEmail = "contacto@turisclick.dev", description = "Actualizada." });
        var admin = await AdminAsync();
        var suspend = await admin.PostAsync($"/api/admin/companies/{account.Company.Id}/suspend", null);
        var reactivate = await admin.PostAsync($"/api/admin/companies/{account.Company.Id}/reactivate", null);

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        Assert.Equal(legacyNit, await QueryAsync(db =>
            db.Companies.AsNoTracking().Where(c => c.Id == account.Company.Id).Select(c => c.LegalDocument).SingleAsync()));
    }

    // ================================================================ categorías (R-07 a R-09)

    [Theory]
    [InlineData("Ab")]
    [InlineData("4x4")]
    [InlineData("!!!")]
    [InlineData("Aventuraaaa")]
    [InlineData("Una categoría con un nombre demasiado largo")]
    [InlineData("Montaña 🏔")]
    [InlineData("Ocul​ta")]
    [InlineData("   ")]
    public async Task Categoria_NombreInvalido_400EnEspanol(string name)
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync("/api/admin/categories", new { name });

        await AssertFieldErrorAsync(response, HttpStatusCode.BadRequest, "name");
    }

    [Fact]
    public async Task Categoria_DescripcionDemasiadoLarga_400()
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync("/api/admin/categories",
            new { name = $"Turismo {UniqueLabel()}", description = new string('d', 301) });

        Assert.Contains("300", await AssertFieldErrorAsync(response, HttpStatusCode.BadRequest, "description"));
    }

    [Fact]
    public async Task Categoria_ConNumerosValidaYNormalizada()
    {
        var admin = await AdminAsync();
        var label = UniqueLabel();

        var response = await admin.PostAsJsonAsync("/api/admin/categories", new { name = $"  Turismo   4x4 {label} " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"Turismo 4x4 {label}", (await response.Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions))!.Name);
    }

    [Fact]
    public async Task Categoria_DuplicadaSinMayusculasNiTildes_409()
    {
        var admin = await AdminAsync();
        var label = UniqueLabel();
        var created = await (await admin.PostAsJsonAsync("/api/admin/categories", new { name = $"Montaña {label}" }))
            .Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);

        var lower = await admin.PostAsJsonAsync("/api/admin/categories", new { name = $"montaña {label}" });
        var accented = await admin.PostAsJsonAsync("/api/admin/categories", new { name = $"MONTÁÑA  {label}" });
        var renameSelf = await admin.PutAsJsonAsync($"/api/admin/categories/{created!.Id}", new { name = $"MONTAÑA {label}" });
        var distinctN = await admin.PostAsJsonAsync("/api/admin/categories", new { name = $"Montana {label}" });

        await AssertFieldErrorAsync(lower, HttpStatusCode.Conflict, "name");
        await AssertFieldErrorAsync(accented, HttpStatusCode.Conflict, "name");
        Assert.Equal(HttpStatusCode.OK, renameSelf.StatusCode);
        Assert.Equal(HttpStatusCode.Created, distinctN.StatusCode); // la ñ es una letra distinta de la n
    }

    [Fact]
    public async Task Categoria_DosAltasSimultaneas_UnaGanaYLaOtraRecibe409()
    {
        var admin = await AdminAsync();
        var name = $"Simultánea {UniqueLabel()}";

        var responses = await Task.WhenAll(
            admin.PostAsJsonAsync("/api/admin/categories", new { name }),
            admin.PostAsJsonAsync("/api/admin/categories", new { name = name.ToLowerInvariant() }));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
    }

    [Fact]
    public async Task Categoria_EnUso_NoSePuedeEliminar()
    {
        var admin = await AdminAsync();
        var label = UniqueLabel();
        var category = await (await admin.PostAsJsonAsync("/api/admin/categories", new { name = $"Interés {label}" }))
            .Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);

        // Una experiencia y un turista la usan.
        var country = await (await admin.PostAsJsonAsync("/api/admin/destinations", new { name = $"Pais {label}", type = "COUNTRY" }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await admin.PostAsJsonAsync("/api/admin/destinations", new { name = $"Region {label}", type = "REGION", parentId = country!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var city = await (await admin.PostAsJsonAsync("/api/admin/destinations", new { name = $"Ciudad {label}", type = "CITY", parentId = region!.Id }))
            .Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var provider = _factory.CreateClient();
        UseBearerToken(provider, (await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), "catuso")).AccessToken);
        var experience = await provider.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour {label}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = city!.Id,
            categoryIds = new[] { category!.Id },
            price = 40,
            currency = "USD",
        });
        Assert.True(experience.IsSuccessStatusCode, await experience.Content.ReadAsStringAsync());
        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "catuso"));
        Assert.Equal(HttpStatusCode.OK, (await tourist.PutAsJsonAsync("/api/tourists/me/preferences",
            new { categoryIds = new[] { category.Id } })).StatusCode);

        var delete = await admin.DeleteAsync($"/api/admin/categories/{category.Id}");

        var body = await JsonAsync(delete);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("CATEGORY_IN_USE", body.GetProperty("errorCode").GetString());
        Assert.Equal(
            $"No se puede eliminar «Interés {label}» porque está asignada a 1 experiencia, y 1 turista la eligió como interés. Quitala de esos elementos antes de eliminarla.",
            body.GetProperty("detail").GetString());
        Assert.True(await QueryAsync(db => db.Categories.AnyAsync(c => c.Id == category.Id)));
    }

    [Fact]
    public async Task Categoria_SinUso_SeElimina()
    {
        var admin = await AdminAsync();
        var category = await (await admin.PostAsJsonAsync("/api/admin/categories", new { name = $"Efímera {UniqueLabel()}" }))
            .Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/categories/{category!.Id}")).StatusCode);
    }

    // ================================================================ persistencia (sin pasar por la API)

    [Fact]
    public async Task Base_RechazaDuplicadosNormalizadosAunqueNoPasenPorLaApi()
    {
        var label = UniqueLabel();
        var nit = NewNit();
        var categoryError = await Assert.ThrowsAsync<PostgresException>(() => QueryAsync(db => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO categories (id, name) VALUES (gen_random_uuid(), {0}), (gen_random_uuid(), {1})",
            $"Persistencia {label}", $"PERSISTÉNCIA  {label}")));
        var nitError = await Assert.ThrowsAsync<PostgresException>(() => QueryAsync(db => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO companies (id, name, legal_document, contact_email) VALUES (gen_random_uuid(), {0}, {1}, 'a@b.bo'), (gen_random_uuid(), {2}, {3}, 'a@b.bo')",
            $"Empresa Uno {label}", nit, $"Empresa Dos {label}", $"{nit[..5]}.{nit[5..]}")));

        Assert.Equal(("23505", "uq_categories_name_key"), (categoryError.SqlState, categoryError.ConstraintName));
        Assert.Equal(("23505", "uq_companies_legal_document_normalized"), (nitError.SqlState, nitError.ConstraintName));
    }

    [Fact]
    public async Task Base_NoDejaBorrarUnaCategoriaEnUsoAunqueNoPaseporLaApi()
    {
        var admin = await AdminAsync();
        var category = await (await admin.PostAsJsonAsync("/api/admin/categories", new { name = $"Protegida {UniqueLabel()}" }))
            .Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);
        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "catfk"));
        await tourist.PutAsJsonAsync("/api/tourists/me/preferences", new { categoryIds = new[] { category!.Id } });

        var error = await Assert.ThrowsAsync<PostgresException>(() => QueryAsync(db =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM categories WHERE id = {0}", category.Id)));

        Assert.Equal("23503", error.SqlState);
    }

    [Fact]
    public async Task Base_TieneLaMigracion0016Aplicada()
    {
        var applied = await QueryAsync(db => db.Database.GetAppliedMigrationsAsync());

        Assert.Contains(applied, m => m.EndsWith("_0016_Fase1_Validaciones", StringComparison.Ordinal));
    }

    // ================================================================ errores y contraseñas (R-12 a R-14)

    [Fact]
    public async Task JsonMalFormado_400EnEspanolSinDetallesTecnicos()
    {
        var admin = await AdminAsync();
        using var content = new StringContent("{\"name\": 123", Encoding.UTF8, "application/json");

        var response = await admin.PostAsync("/api/admin/categories", content);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotMatch(EnglishOrTechnical(), text);
        Assert.Equal("Hay datos para corregir.", JsonDocument.Parse(text).RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ErroresDeOtrosModulos_TambienSalenEnEspanol()
    {
        // Los DTOs de las fases 2 y 3 todavía usan atributos sin mensaje propio: la respuesta igual llega en español.
        var provider = _factory.CreateClient();
        UseBearerToken(provider, (await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), "otrosmod")).AccessToken);

        var response = await provider.PostAsJsonAsync("/api/experiences", new { title = "", description = "", price = -1, currency = "USD" });

        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotMatch(EnglishOrTechnical(), text);
    }

    [Fact]
    public async Task CambioDeContrasena_MenosDe10_400EnElCampo()
    {
        var provider = _factory.CreateClient();
        var account = await RegisterApprovedProviderAsync(provider, _factory.CreateClient(), "clave");
        UseBearerToken(provider, account.AccessToken);

        var response = await provider.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = account.Password, newPassword = "corta123" });

        Assert.Equal("La contraseña nueva tiene que tener al menos 10 caracteres.",
            await AssertFieldErrorAsync(response, HttpStatusCode.BadRequest, "newPassword"));
    }

    [Fact]
    public async Task Login_UnaContrasenaExistenteDeOchoCaracteresSigueSirviendo()
    {
        // D-7: el mínimo de 10 rige al crear o cambiar contraseñas, nunca al entrar.
        var client = _factory.CreateClient();
        var email = $"ocho.{Guid.NewGuid():N}@turisclick.dev";
        Assert.True((await client.PostAsJsonAsync("/api/auth/register",
            new { firstName = "Lucía", lastName = "Fernández", email, password = "ocho1234" })).IsSuccessStatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "ocho1234" });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
