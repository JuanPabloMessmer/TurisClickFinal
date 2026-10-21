using TurisClick.Api.Modules.Auth.Entities;

namespace TurisClick.Api.Modules.Companies.Entities;

/// <summary>docs/domain-model.md §2. Soporta UC-P-01/02/03, UC-A-01/02/03/08.</summary>
public class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string LegalDocument { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public CompanyStatus Status { get; set; } = CompanyStatus.PENDING_APPROVAL;

    /// <summary>ADMIN que aprobó o rechazó la solicitud (mismo campo para ambas acciones).</summary>
    public Guid? ApprovedByUserId { get; set; }
    public User? ApprovedByUser { get; set; }

    /// <summary>Nulo hasta que se aprueba. Permanece nulo si la solicitud se rechaza.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>Solo tiene valor si Status = REJECTED.</summary>
    public string? RejectionReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // ------------------------------------------------------------------ venta de vuelos (Oleada 18)

    /// <summary>La autoriza un ADMIN. Sin esto, la empresa no publica vuelos y los suyos no aparecen en la búsqueda.</summary>
    public bool CanSellFlights { get; set; }

    /// <summary>Registro o licencia que la empresa declaró, tal como figura en su documentación.</summary>
    public string? FlightSellerRegistration { get; set; }

    public DateTimeOffset? FlightSellerAuthorizedAt { get; set; }
    public Guid? FlightSellerAuthorizedByUserId { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
}
