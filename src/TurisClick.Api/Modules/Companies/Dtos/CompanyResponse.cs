namespace TurisClick.Api.Modules.Companies.Dtos;

/// <summary>Vista completa de una empresa — usada tanto por ADMIN (listado/detalle) como por el PROVIDER dueño ("Mi Empresa").</summary>
public class CompanyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string LegalDocument { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Si un ADMIN autorizó a la empresa a vender vuelos (Oleada 18).</summary>
    public bool CanSellFlights { get; set; }

    /// <summary>Registro o licencia declarada al autorizarla. Se conserva aunque se revoque.</summary>
    public string? FlightSellerRegistration { get; set; }

    /// <summary>Cuándo se la autorizó. Nulo si nunca se la autorizó o se revocó.</summary>
    public DateTimeOffset? FlightSellerAuthorizedAt { get; set; }
}
