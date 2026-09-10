namespace TurisClick.Api.Modules.Reservations.Services;

/// <summary>Configuración del proceso de expiración (sección `Reservations:Expiration` de appsettings).</summary>
public class ReservationExpirationOptions
{
    public const string SectionName = "Reservations:Expiration";

    /// <summary>
    /// Permite apagarlo por completo. Los tests de integración lo desactivan para que ningún timer
    /// expire reservas por su cuenta en medio de una prueba — ahí la expiración se invoca a mano.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Cada cuánto se busca. 60s sobra para un hold de 30 minutos.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Cuántas reservas vencidas se procesan por pasada.</summary>
    public int BatchSize { get; set; } = 100;
}
