namespace TurisClick.Api.Modules.Destinations.Dtos;

/// <summary>UC-T-03 — jerarquía pública con conteo de experiencias PUBLISHED por nodo.</summary>
public class PublicDestinationResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public string? ParentName { get; set; }

    /// <summary>Imagen representativa; null si todavía no tiene (el cliente muestra un fallback).</summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Conteo directo (no agregado por jerarquía): experiencias PUBLISHED cuyo DestinationId es
    /// exactamente este nodo. Por la Regla 10 del dominio, siempre es 0 para COUNTRY/REGION, ya que
    /// Experience solo referencia destinos de tipo CITY.
    /// </summary>
    public int PublishedExperienceCount { get; set; }

    /// <summary>
    /// Conteo directo de paquetes PUBLISHED cuyo DestinationId es exactamente este nodo. Existe porque una
    /// ciudad puede tener paquetes sin tener experiencias, y con solo el conteo de experiencias el cliente no
    /// puede distinguir "no hay nada que reservar acá" de "hay paquetes pero ninguna experiencia suelta".
    /// </summary>
    public int PublishedPackageCount { get; set; }
}
