namespace TurisClick.Api.Modules.Experiences.Dtos;

public class ExperienceAvailabilityResponse
{
    public Guid Id { get; set; }
    public Guid ExperienceId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly? StartTime { get; set; }
    public int TotalSlots { get; set; }
    public int ReservedSlots { get; set; }
    public int AvailableSlots { get; set; }
    public string Status { get; set; } = string.Empty;
}
