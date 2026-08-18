namespace TurisClick.Api.Modules.Destinations.Entities;

/// <summary>docs/domain-model.md §3. Jerarquía Country → Region → City, autorreferencial.</summary>
public class Destination
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DestinationType Type { get; set; }

    /// <summary>Nulo solo para Type = COUNTRY.</summary>
    public Guid? ParentId { get; set; }

    public Destination? Parent { get; set; }
    public ICollection<Destination> Children { get; set; } = new List<Destination>();

    public DateTimeOffset CreatedAt { get; set; }
}
