namespace TurisClick.Api.Modules.Flights.Services;

/// <summary>
/// `Flights:Provider` elige la implementación de IFlightProvider en el arranque, igual que `Ai:Provider`
/// con el modelo de lenguaje. "Fake" no depende de red y es el default: Azure y los tests corren con él.
/// </summary>
public class FlightsOptions
{
    public const string SectionName = "Flights";

    public string Provider { get; set; } = "Fake";

    /// <summary>
    /// Si la búsqueda suelta de vuelos se puede usar sin sesión. Sin valor, se decide por el proveedor: con
    /// el falso sí (no consulta a nadie ni cuesta nada), con uno real no — cada búsqueda anónima sería una
    /// consulta a la API del proveedor, con su límite de pedidos. Prenderlo con un proveedor real es una
    /// decisión explícita, que debería venir con un límite de pedidos propio.
    /// </summary>
    public bool? AllowAnonymousSearch { get; set; }

    public bool AnonymousSearchAllowed =>
        AllowAnonymousSearch ?? string.Equals(Provider, "Fake", StringComparison.OrdinalIgnoreCase);

    public DuffelOptions Duffel { get; set; } = new();
}

public class DuffelOptions
{
    public string BaseUrl { get; set; } = "https://api.duffel.com";

    /// <summary>
    /// Token de acceso. **Nunca** se escribe en appsettings ni se loguea: vive en User Secrets localmente
    /// y, el día que haya un entorno desplegado, en Key Vault. Un token de prueba empieza con
    /// `duffel_test_`; el guard de abajo existe para que un token live no entre por accidente.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Cabecera `Duffel-Version` — la API exige versionado explícito en cada request.</summary>
    public string ApiVersion { get; set; } = "v2";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Si está en true (default), el adapter se niega a arrancar con un token que no sea de prueba. Es un
    /// cinturón de seguridad deliberado: una reserva aérea real cuesta dinero de verdad.
    /// </summary>
    public bool RequireTestToken { get; set; } = true;

    public const string TestTokenPrefix = "duffel_test_";

    public bool IsTestToken => AccessToken.StartsWith(TestTokenPrefix, StringComparison.Ordinal);
}
