namespace TurisClick.Api.Modules.Admin.Dtos;

/// <summary>
/// Resumen de plataforma para el "Hoy" del administrador.
///
/// Son números que la base ya sabe contar, no analítica: cuántas empresas operan, qué hay publicado, qué
/// reservas entraron y —lo único que realmente pide acción— qué quedó esperando a alguien. No hay series
/// temporales ni ingresos proyectados porque no tenemos esos datos, y un gráfico inventado en una pantalla de
/// operación es peor que no tener gráfico.
/// </summary>
public class AdminOverviewResponse
{
    public int CompaniesApproved { get; set; }
    public int CompaniesPendingApproval { get; set; }
    public int CompaniesSuspended { get; set; }

    public int ExperiencesPublished { get; set; }
    public int ExperiencesDraft { get; set; }
    public int ExperiencesSuspended { get; set; }

    public int PackagesPublished { get; set; }
    public int PackagesDraft { get; set; }
    public int PackagesWithFlight { get; set; }

    public int ReservationsToday { get; set; }
    public int ReservationsLast7Days { get; set; }
    public int ReservationsPendingPayment { get; set; }
    public int ReservationsConfirmed { get; set; }

    /// <summary>Operadores que todavía no cambiaron la contraseña temporal con la que se los dio de alta.</summary>
    public int ProviderAccountsPendingFirstLogin { get; set; }

    // ---- Lo que pide acción. Si estos números no son cero, alguien tiene trabajo. ----

    /// <summary>Emisiones de pasaje con desenlace sin resolver.</summary>
    public int FlightsAwaitingReconciliation { get; set; }

    /// <summary>Cancelaciones con reembolso pendiente o en revisión.</summary>
    public int CancellationsNeedingReview { get; set; }

    /// <summary>Saldo del libro de pagos por moneda. Nunca un total único entre monedas distintas.</summary>
    public List<AdminMoneyResponse> ChargedByCurrency { get; set; } = [];
    public List<AdminMoneyResponse> RefundedByCurrency { get; set; } = [];
}

public class AdminMoneyResponse
{
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>Una experiencia de cualquier empresa, como la ve el administrador.</summary>
public class AdminExperienceRowResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyStatus { get; set; } = string.Empty;
    public string DestinationName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Categories { get; set; }
    public int FutureAvailabilities { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Un paquete de cualquier empresa, como lo ve el administrador.</summary>
public class AdminPackageRowResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyStatus { get; set; } = string.Empty;
    public string DestinationName { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IncludesFlight { get; set; }
    public string? FlightRoute { get; set; }
    public bool HasCancellationPolicy { get; set; }
    public int Categories { get; set; }
    public int FutureDepartures { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Una reserva de cualquier turista. Lleva lo que hace falta para operar —qué se reservó, de qué empresa, en
/// qué estado está el pasaje, la cancelación y la plata— y **ningún dato de pasajero**: que el administrador
/// exista no es razón para exponer datos personales que el sistema deliberadamente no guarda.
/// </summary>
public class AdminReservationRowResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Nombre del turista. Es quien compró, no un pasajero de vuelo.</summary>
    public string TouristName { get; set; } = string.Empty;

    /// <summary>EXPERIENCE | PACKAGE | MIXED — y si vino de un itinerario del asistente.</summary>
    public string Kind { get; set; } = string.Empty;
    public bool FromAssistant { get; set; }

    public string Summary { get; set; } = string.Empty;
    public List<string> Companies { get; set; } = [];
    public int Travelers { get; set; }

    public List<AdminMoneyResponse> Totals { get; set; } = [];

    /// <summary>Estado del pasaje, si la reserva incluye vuelo.</summary>
    public string? FlightStatus { get; set; }
    public string? FlightRoute { get; set; }

    /// <summary>Estado de la última cancelación, si hubo alguna.</summary>
    public string? CancellationStatus { get; set; }

    /// <summary>Lo efectivamente cobrado y devuelto, por moneda.</summary>
    public List<AdminMoneyResponse> Charged { get; set; } = [];
    public List<AdminMoneyResponse> Refunded { get; set; } = [];
}
