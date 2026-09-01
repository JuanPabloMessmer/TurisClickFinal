using Microsoft.Extensions.Logging;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Categories.Entities;
using TurisClick.Api.Modules.Categories.Repositories;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Repositories;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Ai.Services;

/// <summary>
/// UC-T-12/13/14, UC-AI-01/02/03/04. Orquesta extracción → merge determinístico → retrieval →
/// composición → validación anti-hallucination → persistencia. El LLM (IAiModelClient) nunca decide
/// solo: cada dato crítico que devuelve se re-valida contra lo que el backend ya cargó de Postgres
/// antes de persistir nada (ver docs de la sesión, sección 11).
/// </summary>
public class AiConversationService(
    IAiConversationRepository conversationRepository,
    IAiItineraryRepository itineraryRepository,
    IDestinationRepository destinationRepository,
    ICategoryRepository categoryRepository,
    IAiModelClient aiModelClient,
    IRetrievalService retrievalService,
    ICurrentUserContext currentUser,
    ILogger<AiConversationService> logger,
    TurisClickDbContext db) : IAiConversationService
{
    /// <summary>Cuántos turnos previos de la conversación se le mandan al modelo como contexto — evita prompts descontrolados en conversaciones largas.</summary>
    private const int MaxHistoryTurns = 20;

    public async Task<ConversationResponse> CreateAsync(CancellationToken ct)
    {
        var conversation = new AiConversation
        {
            Id = Guid.NewGuid(),
            TouristId = currentUser.UserId,
            Status = AiConversationStatus.ACTIVE,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await conversationRepository.AddAsync(conversation, ct);
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(conversation.Id, ct);
    }

    public async Task<PagedResult<ConversationSummaryResponse>> ListMineAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await conversationRepository.ListByTouristAsync(currentUser.UserId, page, pageSize, ct);

        return new PagedResult<ConversationSummaryResponse>
        {
            Items = items.Select(ToSummary).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<ConversationResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var conversation = await conversationRepository.GetByIdForReadAsync(id, ct)
            ?? throw new NotFoundAppException("Conversación no encontrada.");

        EnsureOwnsConversation(conversation.TouristId);

        return ToResponse(conversation);
    }

    public async Task<SendMessageResponse> SendMessageAsync(Guid conversationId, SendMessageRequest request, CancellationToken ct)
    {
        var conversation = await conversationRepository.GetByIdForUpdateAsync(conversationId, ct)
            ?? throw new NotFoundAppException("Conversación no encontrada.");

        EnsureOwnsConversation(conversation.TouristId);

        var now = DateTimeOffset.UtcNow;
        var userMessage = request.Content.Trim();

        // Historial ANTES de agregar el mensaje nuevo — se manda aparte como LatestMessage.
        var history = conversation.Messages
            .OrderByDescending(m => m.CreatedAt)
            .Take(MaxHistoryTurns)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ConversationTurn(m.Sender.ToString(), m.Content))
            .ToList();

        AddMessage(conversation.Id, MessageSender.TOURIST, userMessage, now);

        // Vocabulario real (UC-AI-01: el modelo elige entre nombres conocidos, no inventa catálogo).
        var knownDestinations = await destinationRepository.ListAsync(null, DestinationType.CITY, ct);
        var knownCategories = await categoryRepository.ListAsync(ct);
        var destinationByName = knownDestinations.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var categoryByName = knownCategories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var currentSnapshot = BuildSnapshot(conversation);

        PreferenceExtractionResult extraction;
        try
        {
            extraction = await aiModelClient.ExtractPreferencesAsync(
                new PreferenceExtractionRequest(history, userMessage, currentSnapshot,
                    knownDestinations.Select(d => d.Name).ToList(), knownCategories.Select(c => c.Name).ToList(), DateOnly.FromDateTime(now.UtcDateTime)),
                ct);
        }
        catch (Exception ex) when (ex is AiModelUnavailableException or AiModelResponseException)
        {
            logger.LogWarning(ex, "SendMessage {ConversationId}: no se pudo interpretar el mensaje.", conversationId);
            return await FailGracefullyAsync(conversation, now, ct);
        }

        MergePreferences(conversation, extraction, destinationByName, categoryByName);
        conversation.UpdatedAt = now;

        var missingFields = ComputeMissingFields(conversation);

        if (missingFields.Count > 0)
        {
            string reply;
            try
            {
                reply = await aiModelClient.GenerateClarificationReplyAsync(new ClarificationRequest(history, userMessage, missingFields), ct);
            }
            catch (Exception ex) when (ex is AiModelUnavailableException or AiModelResponseException)
            {
                logger.LogWarning(ex, "SendMessage {ConversationId}: no se pudo generar la pregunta de clarificación.", conversationId);
                reply = $"Para seguir necesito que me cuentes: {string.Join(", ", missingFields)}.";
            }

            AddMessage(conversation.Id, MessageSender.AI, reply, now);
            await db.SaveChangesAsync(ct);

            return new SendMessageResponse
            {
                AssistantMessage = reply,
                ParsedPreferences = ToPreferencesResponse(conversation),
                ClarificationNeeded = true,
                MissingInformation = missingFields
            };
        }

        return await GenerateItineraryAsync(conversation, history, userMessage, now, ct);
    }

    public async Task<ItineraryResponse> GetLatestItineraryAsync(Guid conversationId, CancellationToken ct)
    {
        var conversation = await conversationRepository.GetByIdForReadAsync(conversationId, ct)
            ?? throw new NotFoundAppException("Conversación no encontrada.");
        EnsureOwnsConversation(conversation.TouristId);

        var itinerary = await itineraryRepository.GetLatestByConversationIdAsync(conversationId, ct)
            ?? throw new NotFoundAppException("Todavía no se generó ningún itinerario para esta conversación.");

        return ToItineraryResponse(itinerary, conversation.TravelersCount ?? 1);
    }

    public async Task<ItineraryResponse> GetItineraryByIdAsync(Guid itineraryId, CancellationToken ct)
    {
        var itinerary = await itineraryRepository.GetByIdForReadAsync(itineraryId, ct)
            ?? throw new NotFoundAppException("Itinerario no encontrado.");

        if (itinerary.TouristId != currentUser.UserId)
            throw new ForbiddenAppException("Este itinerario no te pertenece.");

        var conversation = await conversationRepository.GetByIdForReadAsync(itinerary.AiConversationId, ct);

        return ToItineraryResponse(itinerary, conversation?.TravelersCount ?? 1);
    }

    // ---- UC-AI-02/03/04: retrieval + composición + validación anti-hallucination ----

    private async Task<SendMessageResponse> GenerateItineraryAsync(
        AiConversation conversation, List<ConversationTurn> history, string userMessage, DateTimeOffset now, CancellationToken ct)
    {
        var tripDurationDays = conversation.DurationDays
            ?? (conversation.StartDate.HasValue && conversation.EndDate.HasValue
                ? conversation.EndDate.Value.DayNumber - conversation.StartDate.Value.DayNumber + 1
                : 1);
        tripDurationDays = Math.Max(tripDurationDays, 1);

        var travelers = conversation.TravelersCount ?? 1;
        var budgetPerPerson = conversation.BudgetTotal.HasValue ? conversation.BudgetTotal.Value / travelers : (decimal?)null;

        var retrieval = await retrievalService.RetrieveAsync(
            new RetrievalQuery(
                conversation.PreferredDestinationId,
                conversation.StartDate,
                conversation.EndDate,
                tripDurationDays,
                budgetPerPerson,
                conversation.BudgetCurrency,
                conversation.Categories.Select(c => c.Id).ToList()),
            ct);

        logger.LogInformation(
            "Ai retrieval {ConversationId}: {ExperienceCount} experiencias, {PackageCount} paquetes candidatos.",
            conversation.Id, retrieval.Experiences.Count, retrieval.Packages.Count);

        if (retrieval.Experiences.Count == 0 && retrieval.Packages.Count == 0)
        {
            const string noMatchesReply = "No encontré experiencias ni paquetes publicados que coincidan con lo que buscás todavía. ¿Querés ajustar el destino, las fechas o el presupuesto?";
            AddMessage(conversation.Id, MessageSender.AI, noMatchesReply, now);
            await db.SaveChangesAsync(ct);

            return new SendMessageResponse
            {
                AssistantMessage = noMatchesReply,
                ParsedPreferences = ToPreferencesResponse(conversation),
                ClarificationNeeded = false,
                Warnings = ["No hay candidatos publicados disponibles para estos criterios."]
            };
        }

        ItineraryCompositionResult composition;
        try
        {
            composition = await aiModelClient.ComposeItineraryAsync(
                new ItineraryCompositionRequest(BuildSnapshot(conversation), tripDurationDays, retrieval.Experiences, retrieval.Packages), ct);
        }
        catch (Exception ex) when (ex is AiModelUnavailableException or AiModelResponseException)
        {
            logger.LogWarning(ex, "SendMessage {ConversationId}: no se pudo componer el itinerario.", conversation.Id);
            return await FailGracefullyAsync(conversation, now, ct);
        }

        var (validItems, warnings) = ValidateComposedItems(composition.Items, retrieval);
        logger.LogInformation(
            "Ai composition {ConversationId}: {Selected} ítems elegidos por el modelo, {Valid} válidos tras la revalidación.",
            conversation.Id, composition.Items.Count, validItems.Count);

        if (validItems.Count == 0)
        {
            const string noValidReply = "No pude armar una propuesta válida con lo que encontré — ¿querés intentar con otros criterios?";
            AddMessage(conversation.Id, MessageSender.AI, noValidReply, now);
            await db.SaveChangesAsync(ct);

            return new SendMessageResponse
            {
                AssistantMessage = noValidReply,
                ParsedPreferences = ToPreferencesResponse(conversation),
                ClarificationNeeded = false,
                Warnings = warnings.Append("El modelo no propuso ningún candidato válido.").ToList()
            };
        }

        var itinerary = new AiItinerary
        {
            Id = Guid.NewGuid(),
            AiConversationId = conversation.Id,
            TouristId = conversation.TouristId,
            Title = string.IsNullOrWhiteSpace(composition.Title) ? null : composition.Title,
            Status = AiItineraryStatus.DRAFT,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var item in validItems)
            itinerary.Items.Add(item);

        await itineraryRepository.AddAsync(itinerary, ct);

        var explanation = string.IsNullOrWhiteSpace(composition.AssistantExplanation)
            ? "Armé una propuesta de itinerario con productos reales disponibles."
            : composition.AssistantExplanation;

        AddMessage(conversation.Id, MessageSender.AI, explanation, now);

        await db.SaveChangesAsync(ct);

        var persisted = await itineraryRepository.GetByIdForReadAsync(itinerary.Id, ct)
            ?? throw new InvalidOperationException("El itinerario recién creado no pudo leerse.");

        return new SendMessageResponse
        {
            AssistantMessage = explanation,
            ParsedPreferences = ToPreferencesResponse(conversation),
            ClarificationNeeded = false,
            Itinerary = ToItineraryResponse(persisted, travelers),
            Warnings = warnings
        };
    }

    /// <summary>
    /// UC-11 (sección de la sesión) — anti-hallucination: cada ítem que devolvió el modelo se valida
    /// contra el set de candidatos que NOSOTROS le dimos. Si el modelo inventó un id, o eligió un id que
    /// no le ofrecimos, el ítem se descarta (nunca se persiste ciego). El precio/moneda que se persisten
    /// siempre vienen del candidato (ya leído fresco de Postgres en este mismo request), nunca de lo que
    /// el modelo pudiera haber repetido en su respuesta.
    /// </summary>
    private static (List<AiItineraryItem> Items, List<string> Warnings) ValidateComposedItems(
        IReadOnlyList<ComposedItem> composedItems, RetrievalResult retrieval)
    {
        var experienceById = retrieval.Experiences.ToDictionary(e => e.Id);
        var packageById = retrieval.Packages.ToDictionary(p => p.Id);

        var items = new List<AiItineraryItem>();
        var warnings = new List<string>();
        var sortOrderByDay = new Dictionary<int, int>();

        foreach (var composed in composedItems)
        {
            var dayNumber = Math.Max(composed.DayNumber, 1);
            var sortOrder = sortOrderByDay.GetValueOrDefault(dayNumber);

            if (string.Equals(composed.ProductType, "EXPERIENCE", StringComparison.OrdinalIgnoreCase)
                && experienceById.TryGetValue(composed.ProductId, out var experience))
            {
                var availabilityId = composed.AvailabilityId is { } expAvailId && experience.Availabilities.Any(a => a.Id == expAvailId)
                    ? expAvailId
                    : experience.Availabilities.OrderBy(a => a.Date).FirstOrDefault()?.Id;

                items.Add(new AiItineraryItem
                {
                    Id = Guid.NewGuid(),
                    DayNumber = dayNumber,
                    SortOrder = sortOrder,
                    ProductType = ProductType.EXPERIENCE,
                    ExperienceId = experience.Id,
                    ExperienceAvailabilityId = availabilityId,
                    EstimatedUnitPrice = experience.Price,
                    Currency = experience.Currency
                });
                sortOrderByDay[dayNumber] = sortOrder + 1;
            }
            else if (string.Equals(composed.ProductType, "PACKAGE", StringComparison.OrdinalIgnoreCase)
                && packageById.TryGetValue(composed.ProductId, out var package))
            {
                var availabilityId = composed.AvailabilityId is { } pkgAvailId && package.Availabilities.Any(a => a.Id == pkgAvailId)
                    ? pkgAvailId
                    : package.Availabilities.OrderBy(a => a.Date).FirstOrDefault()?.Id;

                items.Add(new AiItineraryItem
                {
                    Id = Guid.NewGuid(),
                    DayNumber = dayNumber,
                    SortOrder = sortOrder,
                    ProductType = ProductType.PACKAGE,
                    PackageId = package.Id,
                    PackageAvailabilityId = availabilityId,
                    EstimatedUnitPrice = package.Price,
                    Currency = package.Currency
                });
                sortOrderByDay[dayNumber] = sortOrder + 1;
            }
            else
            {
                warnings.Add($"Se descartó una recomendación del modelo que no correspondía a ningún candidato ofrecido ({composed.ProductType} {composed.ProductId}).");
            }
        }

        return (items, warnings);
    }

    private async Task<SendMessageResponse> FailGracefullyAsync(AiConversation conversation, DateTimeOffset now, CancellationToken ct)
    {
        const string reply = "No pude procesar tu mensaje en este momento (el asistente de IA no está disponible). Probá de nuevo en un momento.";
        AddMessage(conversation.Id, MessageSender.AI, reply, now);
        await db.SaveChangesAsync(ct);

        return new SendMessageResponse
        {
            AssistantMessage = reply,
            ParsedPreferences = ToPreferencesResponse(conversation),
            ClarificationNeeded = false,
            Warnings = ["El proveedor de IA no está disponible en este momento."]
        };
    }

    // ---- Merge determinístico de preferencias (UC-AI-01) ----

    private static void MergePreferences(
        AiConversation conversation, PreferenceExtractionResult extraction,
        IReadOnlyDictionary<string, Destination> destinationByName, IReadOnlyDictionary<string, Category> categoryByName)
    {
        if (extraction.DestinationMention is { } destinationName && destinationByName.TryGetValue(destinationName, out var destination))
            conversation.PreferredDestinationId = destination.Id;

        foreach (var categoryName in extraction.CategoryMentions)
        {
            if (categoryByName.TryGetValue(categoryName, out var category) && conversation.Categories.All(c => c.Id != category.Id))
                conversation.Categories.Add(category);
        }

        if (extraction.StartDate.HasValue) conversation.StartDate = extraction.StartDate;
        if (extraction.EndDate.HasValue) conversation.EndDate = extraction.EndDate;
        if (extraction.DurationDays.HasValue) conversation.DurationDays = extraction.DurationDays;
        if (extraction.TravelersCount.HasValue) conversation.TravelersCount = extraction.TravelersCount;

        if (extraction.BudgetAmount.HasValue)
        {
            var travelers = extraction.TravelersCount ?? conversation.TravelersCount ?? 1;
            conversation.BudgetTotal = extraction.BudgetIsPerPerson ? extraction.BudgetAmount.Value * travelers : extraction.BudgetAmount.Value;
            conversation.BudgetCurrency = extraction.BudgetCurrency ?? conversation.BudgetCurrency ?? "USD";
        }

        if (!string.IsNullOrWhiteSpace(extraction.RestrictionsNotes))
            conversation.RestrictionsNotes = extraction.RestrictionsNotes;
    }

    /// <summary>
    /// Campos obligatorios para poder buscar disponibilidad real: destino, fechas-o-duración y
    /// viajeros. El resto (presupuesto, categorías, restricciones) son preferencias opcionales — no
    /// bloquean el retrieval (docs de la sesión, sección 8: "no quiero 10 preguntas innecesarias").
    /// </summary>
    private static List<string> ComputeMissingFields(AiConversation conversation)
    {
        var missing = new List<string>();

        if (conversation.PreferredDestinationId is null)
            missing.Add("destino");

        if (!(conversation.StartDate.HasValue && conversation.EndDate.HasValue) && conversation.DurationDays is null)
            missing.Add("fechas o duración del viaje");

        if (conversation.TravelersCount is null)
            missing.Add("cantidad de viajeros");

        return missing;
    }

    private static ExtractedPreferencesSnapshot BuildSnapshot(AiConversation conversation) => new(
        conversation.PreferredDestination?.Name,
        conversation.StartDate,
        conversation.EndDate,
        conversation.DurationDays,
        conversation.TravelersCount,
        conversation.BudgetTotal,
        conversation.BudgetCurrency,
        conversation.Categories.Select(c => c.Name).ToList(),
        conversation.RestrictionsNotes);

    /// <summary>
    /// Agrega el mensaje directamente vía el DbSet, NO conversation.Messages.Add(...) sobre la
    /// navegación: con una colección 1-a-N ya trackeada, EF Core puede confundir un Add() con un
    /// UPDATE fantasma y SaveChanges lanza DbUpdateConcurrencyException al no encontrar esa fila
    /// inexistente (mismo bug encontrado y documentado en Oleada 4 — ver PackageService.UpdateAsync).
    /// Pasar por el DbSet evita la ambigüedad.
    /// </summary>
    private void AddMessage(Guid conversationId, MessageSender sender, string content, DateTimeOffset createdAt) =>
        db.Set<AiMessage>().Add(new AiMessage { Id = Guid.NewGuid(), AiConversationId = conversationId, Sender = sender, Content = content, CreatedAt = createdAt });

    private void EnsureOwnsConversation(Guid touristId)
    {
        if (touristId != currentUser.UserId)
            throw new ForbiddenAppException("Esta conversación no te pertenece.");
    }

    private static ConversationSummaryResponse ToSummary(AiConversation conversation) => new()
    {
        Id = conversation.Id,
        Status = conversation.Status.ToString(),
        PreferredDestinationName = conversation.PreferredDestination?.Name,
        StartDate = conversation.StartDate,
        EndDate = conversation.EndDate,
        CreatedAt = conversation.CreatedAt,
        UpdatedAt = conversation.UpdatedAt
    };

    private static ConversationResponse ToResponse(AiConversation conversation) => new()
    {
        Id = conversation.Id,
        Status = conversation.Status.ToString(),
        Preferences = ToPreferencesResponse(conversation),
        Messages = conversation.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageResponse { Id = m.Id, Sender = m.Sender.ToString(), Content = m.Content, CreatedAt = m.CreatedAt })
            .ToList(),
        CreatedAt = conversation.CreatedAt,
        UpdatedAt = conversation.UpdatedAt
    };

    private static PreferencesResponse ToPreferencesResponse(AiConversation conversation) => new()
    {
        PreferredDestinationId = conversation.PreferredDestinationId,
        PreferredDestinationName = conversation.PreferredDestination?.Name,
        StartDate = conversation.StartDate,
        EndDate = conversation.EndDate,
        DurationDays = conversation.DurationDays,
        TravelersCount = conversation.TravelersCount,
        BudgetTotal = conversation.BudgetTotal,
        BudgetCurrency = conversation.BudgetCurrency,
        RestrictionsNotes = conversation.RestrictionsNotes,
        Categories = conversation.Categories.Select(c => new CategoryResponse { Id = c.Id, Name = c.Name, Description = c.Description }).ToList()
    };

    private static ItineraryResponse ToItineraryResponse(AiItinerary itinerary, int travelers) => new()
    {
        Id = itinerary.Id,
        AiConversationId = itinerary.AiConversationId,
        Title = itinerary.Title,
        Status = itinerary.Status.ToString(),
        Version = itinerary.Version,
        Items = itinerary.Items
            .OrderBy(i => i.DayNumber).ThenBy(i => i.SortOrder)
            .Select(i => new ItineraryItemResponse
            {
                Id = i.Id,
                DayNumber = i.DayNumber,
                SortOrder = i.SortOrder,
                ProductType = i.ProductType.ToString(),
                ExperienceId = i.ExperienceId,
                ExperienceTitle = i.Experience?.Title,
                PackageId = i.PackageId,
                PackageTitle = i.Package?.Title,
                Date = i.ExperienceAvailability?.Date ?? i.PackageAvailability?.DepartureDate,
                EstimatedUnitPrice = i.EstimatedUnitPrice,
                Currency = i.Currency,
                Travelers = travelers,
                Subtotal = i.EstimatedUnitPrice * travelers
            }).ToList(),
        Totals = [.. itinerary.Items
            .GroupBy(i => i.Currency)
            .Select(g => new ItineraryTotalResponse { Currency = g.Key, Amount = g.Sum(i => i.EstimatedUnitPrice * travelers) })],
        CreatedAt = itinerary.CreatedAt,
        UpdatedAt = itinerary.UpdatedAt
    };
}
