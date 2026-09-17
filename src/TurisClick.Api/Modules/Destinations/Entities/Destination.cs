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

    /// <summary>
    /// Imagen representativa (URL absoluta, p. ej. Wikimedia Commons). Solo la URL: nunca blobs/base64 en
    /// Postgres. La atribución (autor/licencia/fuente) vive versionada en tools/demo-catalog.
    /// </summary>
    public string? ImageUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
