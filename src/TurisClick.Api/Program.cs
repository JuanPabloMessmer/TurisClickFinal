using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Database.Seed;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Admin;
using TurisClick.Api.Modules.Ai;
using TurisClick.Api.Modules.Preferences;
using TurisClick.Api.Modules.Auth;
using TurisClick.Api.Modules.Categories;
using TurisClick.Api.Modules.Companies;
using TurisClick.Api.Modules.Destinations;
using TurisClick.Api.Modules.Experiences;
using TurisClick.Api.Modules.Packages;
using TurisClick.Api.Modules.Reservations;
using TurisClick.Api.Shared.Exceptions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/turisclick-.log", rollingInterval: RollingInterval.Day));

    // ---- Base de datos ----
    // Lectura perezosa (vía IConfiguration resuelto en el momento de crear el DbContext, no acá arriba):
    // así WebApplicationFactory puede sobreescribir la connection string en tests de integración
    // sin pelear con el orden de construcción de ConfigurationManager.
    builder.Services.AddDbContext<TurisClickDbContext>((sp, options) =>
    {
        var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Falta ConnectionStrings:DefaultConnection. Configurala con: dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"...\"");
        options.UseNpgsql(connectionString);
    });

    // ---- JWT ----
    builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // Mismo motivo que arriba: se lee builder.Configuration recién acá adentro (delegate diferido
            // por ASP.NET Core hasta la primera request), no en variables capturadas antes de Build().
            var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
                ?? throw new InvalidOperationException("Falta la sección Jwt en appsettings.json.");
            var jwtKey = builder.Configuration["Jwt:Key"]
                ?? throw new InvalidOperationException(
                    "Falta Jwt:Key. Configurala con: dotnet user-secrets set \"Jwt:Key\" \"...\"");

            // Sin esto, JwtBearerHandler remapea "sub" -> ClaimTypes.NameIdentifier automáticamente,
            // rompiendo la lectura de JwtRegisteredClaimNames.Sub en CurrentUserContext.
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    // ---- CORS (frontend local — Backoffice hoy, Tourist Mobile más adelante) ----
    // Orígenes leídos de configuración (Cors:AllowedOrigins), nunca hardcodeados: en Development,
    // appsettings.Development.json ya trae el puerto por defecto de Vite (5173); en otros ambientes
    // se configura por variable de entorno/user-secrets sin tocar código.
    var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Frontend", policy =>
        {
            if (corsAllowedOrigins.Length > 0)
                policy.WithOrigins(corsAllowedOrigins).AllowAnyHeader().AllowAnyMethod();
            // Sin AllowCredentials(): la autenticación viaja por header Authorization (Bearer), no por
            // cookies, así que no hace falta habilitar credenciales cross-origin.
        });
    });

    // ---- Autorización por rol (ADMIN / PROVIDER / TOURIST) ----
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("RequireAdmin", p => p.RequireRole("ADMIN"));
        options.AddPolicy("RequireProvider", p => p.RequireRole("PROVIDER"));
        options.AddPolicy("RequireTourist", p => p.RequireRole("TOURIST"));
    });

    // ---- Infraestructura + módulos ----
    builder.Services.AddSecurityInfrastructure();
    builder.Services.AddAuthModule();
    builder.Services.AddDestinationsModule();
    builder.Services.AddCategoriesModule();
    builder.Services.AddCompaniesModule();
    builder.Services.AddExperiencesModule();
    builder.Services.AddPackagesModule();
    builder.Services.AddReservationsModule(builder.Configuration);
builder.Services.AddAdminModule();
    builder.Services.AddPreferencesModule();
    builder.Services.AddAiModule(builder.Configuration);

    // ---- Manejo global de errores ----
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services.AddControllers();

    // ---- Swagger/OpenAPI ----
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "TurisClick API", Version = "v1" });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Ingresá el access token JWT (sin el prefijo 'Bearer ')."
        });
        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
        });
    });

    var app = builder.Build();

    app.UseExceptionHandler();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();

    app.UseCors("Frontend");

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // ---- Seed de datos de desarrollo ----
    // Doble gate a propósito (AND, no OR): solo corre si el entorno ES Development Y el flag está
    // explícito en config — nunca en Production, ni por accidente si alguien copia el flag a otro
    // appsettings. Ver Infrastructure/Database/Seed/DevelopmentSeeder.cs.
    if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Seed:Enabled"))
        await DevelopmentSeeder.SeedAsync(app.Services);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException: lo lanza `dotnet ef` a propósito al construir el host para leer el DbContext.
    Log.Fatal(ex, "TurisClick.Api terminó inesperadamente durante el arranque");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Necesaria para que WebApplicationFactory (tests de integración) encuentre el entry point.</summary>
public partial class Program;
