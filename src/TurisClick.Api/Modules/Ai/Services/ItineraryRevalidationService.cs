using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Reservations.Entities;

namespace TurisClick.Api.Modules.Ai.Services;

/// <summary>
/// 100% determinístico: el LLM no participa de la revalidación. Dos queries (experiencias + paquetes
/// referenciados) y comparación en memoria contra el snapshot.
/// </summary>
public class ItineraryRevalidationService(IAiCatalogRepository catalogRepository) : IItineraryRevalidationService
{
    public async Task<ItineraryRevalidationResult> RevalidateAsync(AiItinerary itinerary, CancellationToken ct)
    {
        if (itinerary.Items.Count == 0)
            return ItineraryRevalidationResult.Empty;

        var experienceIds = itinerary.Items.Where(i => i.ExperienceId.HasValue).Select(i => i.ExperienceId!.Value).Distinct().ToList();
        var packageIds = itinerary.Items.Where(i => i.PackageId.HasValue).Select(i => i.PackageId!.Value).Distinct().ToList();

        var experiences = (await catalogRepository.GetExperiencesByIdsAsync(experienceIds, ct)).ToDictionary(e => e.Id);
        var packages = (await catalogRepository.GetPackagesByIdsAsync(packageIds, ct)).ToDictionary(p => p.Id);

        var byItemId = new Dictionary<Guid, ItemRevalidation>();
        var globalWarnings = new List<string>();

        foreach (var item in itinerary.Items)
        {
            var revalidation = item.ProductType == ProductType.EXPERIENCE
                ? RevalidateExperience(item, experiences)
                : RevalidatePackage(item, packages);

            byItemId[item.Id] = revalidation;
            globalWarnings.AddRange(revalidation.Warnings);
        }

        return new ItineraryRevalidationResult(byItemId, globalWarnings);
    }

    private static ItemRevalidation RevalidateExperience(AiItineraryItem item, IReadOnlyDictionary<Guid, Experience> experiences)
    {
        var warnings = new List<string>();

        if (item.ExperienceId is not { } experienceId || !experiences.TryGetValue(experienceId, out var experience))
            return Missing(item, warnings);

        var title = experience.Title;
        var isPublished = experience.Status == PublicationStatus.PUBLISHED;
        if (!isPublished)
            warnings.Add($"\"{title}\" ya no está publicada por el proveedor.");

        // El slot concreto propuesto; si la propuesta nunca fijó uno, sirve cualquiera abierto con cupo.
        var slot = item.ExperienceAvailabilityId is { } slotId
            ? experience.Availabilities.FirstOrDefault(a => a.Id == slotId)
            : experience.Availabilities.FirstOrDefault(a => a.Status == AvailabilitySlotStatus.OPEN && a.AvailableSlots > 0);

        var availabilityExists = slot is not null && slot.Status == AvailabilitySlotStatus.OPEN;
        var hasCapacity = slot is not null && slot.AvailableSlots > 0;

        if (slot is null)
            warnings.Add($"\"{title}\" ya no tiene la fecha que te habíamos propuesto.");
        else if (!availabilityExists)
            warnings.Add($"\"{title}\" cerró la disponibilidad del {slot.Date:yyyy-MM-dd}.");
        else if (!hasCapacity)
            warnings.Add($"\"{title}\" se quedó sin cupos para el {slot.Date:yyyy-MM-dd}.");

        AddPriceWarning(warnings, title, item, experience.Price, experience.Currency);

        return new ItemRevalidation(
            item.Id, ProductExists: true, isPublished, availabilityExists, hasCapacity,
            experience.Price, experience.Currency, slot?.AvailableSlots, warnings);
    }

    private static ItemRevalidation RevalidatePackage(AiItineraryItem item, IReadOnlyDictionary<Guid, Package> packages)
    {
        var warnings = new List<string>();

        if (item.PackageId is not { } packageId || !packages.TryGetValue(packageId, out var package))
            return Missing(item, warnings);

        var title = package.Title;
        var isPublished = package.Status == PublicationStatus.PUBLISHED;
        if (!isPublished)
            warnings.Add($"El paquete \"{title}\" ya no está publicado por el proveedor.");

        var slot = item.PackageAvailabilityId is { } slotId
            ? package.Availabilities.FirstOrDefault(a => a.Id == slotId)
            : package.Availabilities.FirstOrDefault(a => a.Status == AvailabilitySlotStatus.OPEN && a.AvailableSlots > 0);

        var availabilityExists = slot is not null && slot.Status == AvailabilitySlotStatus.OPEN;
        var hasCapacity = slot is not null && slot.AvailableSlots > 0;

        if (slot is null)
            warnings.Add($"El paquete \"{title}\" ya no tiene la salida que te habíamos propuesto.");
        else if (!availabilityExists)
            warnings.Add($"El paquete \"{title}\" cerró la salida del {slot.DepartureDate:yyyy-MM-dd}.");
        else if (!hasCapacity)
            warnings.Add($"El paquete \"{title}\" se quedó sin cupos para el {slot.DepartureDate:yyyy-MM-dd}.");

        AddPriceWarning(warnings, $"el paquete \"{title}\"", item, package.Price, package.Currency);

        return new ItemRevalidation(
            item.Id, ProductExists: true, isPublished, availabilityExists, hasCapacity,
            package.Price, package.Currency, slot?.AvailableSlots, warnings);
    }

    /// <summary>
    /// Solo se avisa de un cambio de precio cuando la moneda es la MISMA: comparar 80 USD contra 95 EUR
    /// exigiría una conversión que TurisClick no hace (sección 10).
    /// </summary>
    private static void AddPriceWarning(List<string> warnings, string label, AiItineraryItem item, decimal currentPrice, string currentCurrency)
    {
        if (!string.Equals(currentCurrency, item.Currency, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"{label} ahora se vende en {currentCurrency} y tu propuesta estaba en {item.Currency}.");
            return;
        }

        if (currentPrice != item.EstimatedUnitPrice)
            warnings.Add($"El precio de {label} cambió de {item.Currency} {item.EstimatedUnitPrice:0.##} a {currentCurrency} {currentPrice:0.##}.");
    }

    private static ItemRevalidation Missing(AiItineraryItem item, List<string> warnings)
    {
        warnings.Add("Uno de los componentes de tu itinerario ya no existe en el catálogo.");
        return new ItemRevalidation(
            item.Id, ProductExists: false, IsPublished: false, AvailabilityExists: false, HasCapacity: false,
            CurrentPrice: null, CurrentCurrency: null, AvailableSlots: null, warnings);
    }
}
