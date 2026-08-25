namespace TurisClick.Api.Modules.Experiences.Entities;

/// <summary>
/// docs/domain-model.md — compartido conceptualmente por Experience y Package. Vive acá porque
/// Experience es su primer consumidor real (Oleada 2); Package (Oleada 4) lo referenciará desde este
/// mismo namespace en vez de duplicarlo.
/// </summary>
public enum PublicationStatus
{
    DRAFT,
    PUBLISHED,
    UNPUBLISHED,
    SUSPENDED
}
