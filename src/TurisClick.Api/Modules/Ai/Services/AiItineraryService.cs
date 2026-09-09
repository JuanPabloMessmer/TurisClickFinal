using System.Globalization;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Ai.Services;

public class AiItineraryService(
    IAiItineraryRepository itineraryRepository,
    IAiConversationRepository conversationRepository,
    IAiCatalogRepository catalogRepository,
    IItineraryRevalidationService revalidationService,
    IAiModelClient aiModelClient,
    ICurrentUserContext currentUser,
    ILogger<AiItineraryService> logger,
    TurisClickDbContext db) : IAiItineraryService
{
    public async Task<ItineraryResponse> GetByIdAsync(Guid itineraryId, CancellationToken ct)
    {
        var itinerary = await LoadOwnedAsync(itineraryId, ct);

        // UC-T-17 / sección 9: retomar NO asume que la propuesta sigue vigente.
        var revalidation = await revalidationService.RevalidateAsync(itinerary, ct);

        logger.LogInformation(
            "Ai revalidation {ItineraryId}: {Invalid} de {Total} componentes ya no son reservables, {Warnings} avisos.",
            itinerary.Id, revalidation.ByItemId.Values.Count(v => !v.IsValid), itinerary.Items.Count, revalidation.Warnings.Count);

        return AiItineraryMapper.ToResponse(itinerary, await TravelersOfAsync(itinerary, ct), revalidation);
    }

    public async Task<ItineraryResponse> SaveAsync(Guid itineraryId, CancellationToken ct)
    {
        var tracked = await itineraryRepository.GetByIdForUpdateAsync(itineraryId, ct)
            ?? throw new NotFoundAppException("Itinerario no encontrado.");

        EnsureOwns(tracked.TouristId);

        // Guardar NO es reservar (sección 19): no toca cupos, no congela precio, no crea Reservation.
        switch (tracked.Status)
        {
            case AiItineraryStatus.DRAFT:
                tracked.Status = AiItineraryStatus.SAVED;
                tracked.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                break;

            case AiItineraryStatus.SAVED:
                // Idempotente: guardar dos veces es un no-op, no un error (sección 14).
                break;

            case AiItineraryStatus.BOOKED:
                throw new ConflictAppException("Este itinerario ya fue reservado y no puede volver a guardarse como borrador.");

            case AiItineraryStatus.DISCARDED:
                throw new ConflictAppException("Este itinerario fue descartado y no puede guardarse.");
        }

        return await GetByIdAsync(itineraryId, ct);
    }

    public async Task<PagedResult<SavedItinerarySummaryResponse>> ListSavedMineAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await itineraryRepository.ListSavedByTouristAsync(currentUser.UserId, page, pageSize, ct);

        // Un solo lookup de conversaciones para no pegarle a la base una vez por fila.
        var travelersByConversation = new Dictionary<Guid, int>();
        foreach (var conversationId in items.Select(i => i.AiConversationId).Distinct())
        {
            var conversation = await conversationRepository.GetByIdForReadAsync(conversationId, ct);
            travelersByConversation[conversationId] = conversation?.TravelersCount ?? 1;
        }

        return new PagedResult<SavedItinerarySummaryResponse>
        {
            Items = items
                .Select(i => AiItineraryMapper.ToSummary(i, travelersByConversation.GetValueOrDefault(i.AiConversationId, 1)))
                .ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<ItemExplanationResponse> GetItemExplanationAsync(Guid itineraryId, Guid itemId, CancellationToken ct)
    {
        var itinerary = await LoadOwnedAsync(itineraryId, ct);

        var item = itinerary.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new NotFoundAppException("Ese componente no pertenece a este itinerario.");

        var conversation = await conversationRepository.GetByIdForReadAsync(itinerary.AiConversationId, ct);
        var preferences = BuildSnapshot(conversation);

        var view = ToView(item);
        var facts = await BuildFactsAsync(item, itinerary, conversation, ct);

        string explanation;
        try
        {
            explanation = await aiModelClient.GenerateItemExplanationAsync(new ItemExplanationRequest(preferences, view, facts), ct);
        }
        catch (Exception ex) when (ex is AiModelUnavailableException or AiModelResponseException)
        {
            // Sin modelo disponible se devuelven los hechos crudos: son verdaderos igual, solo menos
            // redactados. Nunca se inventa una explicación de reemplazo.
            logger.LogWarning(ex, "Ai explanation {ItineraryId}/{ItemId}: el proveedor no respondió, se devuelven los hechos sin redactar.", itineraryId, itemId);
            explanation = string.Join(" ", facts);
        }

        return new ItemExplanationResponse
        {
            ItineraryId = itinerary.Id,
            ItemId = item.Id,
            Explanation = explanation,
            Facts = facts
        };
    }

    /// <summary>
    /// UC-AI-06 — TODA la materia prima de la explicación se calcula acá, desde Postgres. El modelo solo
    /// redacta esto: si un dato no está en esta lista, no puede aparecer en la explicación.
    /// </summary>
    private async Task<List<string>> BuildFactsAsync(
        AiItineraryItem item, AiItinerary itinerary, AiConversation? conversation, CancellationToken ct)
    {
        var facts = new List<string>();

        var interestNames = conversation?.Categories.Select(c => c.Name).ToList() ?? [];
        var tripDays = conversation?.DurationDays
            ?? (conversation?.StartDate is { } s && conversation.EndDate is { } e ? e.DayNumber - s.DayNumber + 1 : (int?)null);
        var travelers = conversation?.TravelersCount;

        string? destinationName = null;
        List<string> categoryNames = [];
        decimal? currentPrice = null;
        string? currentCurrency = null;
        DateOnly? date = null;
        int? availableSlots = null;

        if (item.ProductType == ProductType.EXPERIENCE && item.ExperienceId is { } experienceId)
        {
            var experience = (await catalogRepository.GetExperiencesByIdsAsync([experienceId], ct)).FirstOrDefault();
            if (experience is not null)
            {
                destinationName = experience.Destination?.Name;
                categoryNames = experience.Categories.Select(c => c.Name).ToList();
                currentPrice = experience.Price;
                currentCurrency = experience.Currency;

                var slot = item.ExperienceAvailabilityId is { } slotId
                    ? experience.Availabilities.FirstOrDefault(a => a.Id == slotId)
                    : null;
                date = slot?.Date;
                availableSlots = slot?.AvailableSlots;

                if (experience.DurationMinutes is { } minutes)
                    facts.Add($"Es una experiencia de {minutes} minutos.");
            }
        }
        else if (item.ProductType == ProductType.PACKAGE && item.PackageId is { } packageId)
        {
            var package = (await catalogRepository.GetPackagesByIdsAsync([packageId], ct)).FirstOrDefault();
            if (package is not null)
            {
                destinationName = package.Destination?.Name;
                categoryNames = package.Categories.Select(c => c.Name).ToList();
                currentPrice = package.Price;
                currentCurrency = package.Currency;

                var slot = item.PackageAvailabilityId is { } slotId
                    ? package.Availabilities.FirstOrDefault(a => a.Id == slotId)
                    : null;
                date = slot?.DepartureDate;
                availableSlots = slot?.AvailableSlots;

                facts.Add(tripDays is { } days
                    ? $"Es un paquete de {package.DurationDays} día(s) y tu viaje dura {days} día(s), así que cubre {Math.Min(package.DurationDays, days)} de ellos."
                    : $"Es un paquete de {package.DurationDays} día(s).");
            }
        }

        facts.Add($"Está propuesto para el día {item.DayNumber} de un itinerario de {itinerary.Items.Count} componente(s).");

        if (destinationName is not null)
            facts.Add($"Se realiza en {destinationName}.");

        var shared = categoryNames.Where(c => interestNames.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        if (shared.Count > 0)
            facts.Add($"Coincide con los intereses que indicaste: {string.Join(", ", shared)}.");
        else if (categoryNames.Count > 0)
            facts.Add($"Sus categorías en el catálogo son: {string.Join(", ", categoryNames)}.");

        if (date is { } proposedDate)
        {
            var fact = $"Tiene disponibilidad para el {proposedDate:yyyy-MM-dd}";
            fact += conversation?.StartDate is { } start && conversation.EndDate is { } end && proposedDate >= start && proposedDate <= end
                ? $", dentro de tus fechas de viaje ({start:yyyy-MM-dd} a {end:yyyy-MM-dd})."
                : ".";
            facts.Add(fact);
        }

        if (availableSlots is { } slots)
        {
            facts.Add(travelers is { } count && count > 0
                ? $"Quedan {slots} cupo(s) para esa fecha y ustedes son {count} viajero(s)."
                : $"Quedan {slots} cupo(s) para esa fecha.");
        }

        facts.Add($"El precio con el que se armó la propuesta es {item.Currency} {item.EstimatedUnitPrice.ToString("0.##", CultureInfo.InvariantCulture)} por persona.");

        // Multi-moneda (sección 10): solo se compara contra el presupuesto si la moneda es la misma.
        if (conversation?.BudgetTotal is { } budgetTotal && conversation.BudgetCurrency is { } budgetCurrency)
        {
            var budgetPerPerson = budgetTotal / Math.Max(travelers ?? 1, 1);
            facts.Add(string.Equals(budgetCurrency, item.Currency, StringComparison.OrdinalIgnoreCase)
                ? $"Tu presupuesto es {budgetCurrency} {budgetPerPerson.ToString("0.##", CultureInfo.InvariantCulture)} por persona, así que este componente {(item.EstimatedUnitPrice <= budgetPerPerson ? "entra" : "queda por encima")}."
                : $"No se puede comparar con tu presupuesto porque está en {budgetCurrency} y este componente en {item.Currency} (TurisClick no convierte monedas).");
        }

        if (currentPrice is { } livePrice && currentCurrency is { } liveCurrency
            && string.Equals(liveCurrency, item.Currency, StringComparison.OrdinalIgnoreCase)
            && livePrice != item.EstimatedUnitPrice)
        {
            facts.Add($"El precio actual en el catálogo es {liveCurrency} {livePrice.ToString("0.##", CultureInfo.InvariantCulture)}, distinto del de la propuesta.");
        }

        return facts;
    }

    private async Task<AiItinerary> LoadOwnedAsync(Guid itineraryId, CancellationToken ct)
    {
        var itinerary = await itineraryRepository.GetByIdForReadAsync(itineraryId, ct)
            ?? throw new NotFoundAppException("Itinerario no encontrado.");

        EnsureOwns(itinerary.TouristId);
        return itinerary;
    }

    private async Task<int> TravelersOfAsync(AiItinerary itinerary, CancellationToken ct)
    {
        var conversation = await conversationRepository.GetByIdForReadAsync(itinerary.AiConversationId, ct);
        return conversation?.TravelersCount ?? 1;
    }

    private void EnsureOwns(Guid touristId)
    {
        if (touristId != currentUser.UserId)
            throw new ForbiddenAppException("Este itinerario no te pertenece.");
    }

    private static ExtractedPreferencesSnapshot BuildSnapshot(AiConversation? conversation) => new(
        conversation?.PreferredDestination?.Name,
        conversation?.StartDate,
        conversation?.EndDate,
        conversation?.DurationDays,
        conversation?.TravelersCount,
        conversation?.BudgetTotal,
        conversation?.BudgetCurrency,
        conversation?.Categories.Select(c => c.Name).ToList() ?? [],
        conversation?.RestrictionsNotes);

    private static CurrentItineraryItemView ToView(AiItineraryItem item) => new(
        item.Id,
        item.DayNumber,
        item.ProductType.ToString(),
        item.Experience?.Title ?? item.Package?.Title ?? "Componente",
        item.Experience?.Categories.Select(c => c.Name).ToList() ?? item.Package?.Categories.Select(c => c.Name).ToList() ?? [],
        item.EstimatedUnitPrice,
        item.Currency,
        item.ExperienceAvailability?.Date ?? item.PackageAvailability?.DepartureDate);
}
