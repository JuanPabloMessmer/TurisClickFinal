namespace TurisClick.Api.Modules.Packages.Dtos;

public class PackageAvailabilityResponse
{
    public Guid Id { get; set; }
    public Guid PackageId { get; set; }
    public DateOnly DepartureDate { get; set; }
    public int TotalSlots { get; set; }
    public int ReservedSlots { get; set; }
    public int AvailableSlots { get; set; }
    public string Status { get; set; } = string.Empty;
}
