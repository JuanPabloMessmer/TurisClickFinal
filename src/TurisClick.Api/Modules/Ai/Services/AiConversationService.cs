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
    IItineraryRevalidationService revalidationService,
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

        // UC-T-15/UC-AI-05: si ya hay una propuesta vigente, este mensaje puede ser un AJUSTE sobre ella
        // y no una búsqueda nueva. Quién decide qué se conserva y qué se reemplaza es el backend
        // (determinístico); el modelo solo clasifica la intención y señala ítems por id.
        var currentItinerary = await itineraryRepository.GetLatestByConversationIdAsync(conversation.Id, ct);
        var plan = currentItinerary is null
            ? null
            : await BuildIterationPlanAsync(conversation, currentItinerary, history, userMessage, knownCategories, categoryByName, ct);

        return await GenerateItineraryAsync(conversation, history, userMessage, now, plan, ct);
    }

    /// <summary>
    /// Traduce la intención del modelo a un plan concreto: qué ítems se conservan, cuáles salen y qué
    /// productos no deben volver a ofrecerse. Todo es determinístico salvo la clasificación de la
    /// instrucción, y cualquier id que el modelo devuelva y no pertenezca al itinerario vigente se
    /// descarta (misma barrera anti-hallucination que con los candidatos).
    /// </summary>
    private async Task<IterationPlan?> BuildIterationPlanAsync(
        AiConversation conversation, AiItinerary current, List<ConversationTurn> history, string userMessage,
        List<Category> knownCategories, IReadOnlyDictionary<string, Category> categoryByName, CancellationToken ct)
    {
        ModificationIntentResult intent;
        try
        {
            intent = await aiModelClient.InterpretModificationAsync(
                new ModificationInterpretationRequest(
                    history, userMessage, BuildSnapshot(conversation),
                    current.Items.Select(ToItemView).ToList(),
                    knownCategories.Select(c => c.Name).ToList()),
                ct);
        }
        catch (Exception ex) when (ex is AiModelUnavailableException or AiModelResponseException)
        {
            // Sin interpretación no se asume un ajuste: se trata como búsqueda nueva (comportamiento
            // Oleada 5), que es el camino seguro — nunca se borra el itinerario por un fallo del modelo.
            logger.LogWarning(ex, "SendMessage {ConversationId}: no se pudo interpretar el ajuste, se trata como búsqueda nueva.", conversation.Id);
            return null;
        }

        if (intent.Action == ModificationAction.NONE)
            return null;

        // Categorías que el intérprete detectó y la extracción no llegó a fusionar.
        foreach (var categoryName in intent.AddCategoryNames)
        {
            if (categoryByName.TryGetValue(categoryName, out var category) && conversation.Categories.All(c => c.Id != category.Id))
                conversation.Categories.Add(category);
        }

        var itemsById = current.Items.ToDictionary(i => i.Id);
        // Solo ids que realmente están en el itinerario vigente — el resto es alucinación del modelo.
        var targeted = intent.TargetItemIds
            .Where(itemsById.ContainsKey)
            .Select(id => itemsById[id])
            .ToList();

        var discardedTargets = intent.TargetItemIds.Count - targeted.Count;
        if (discardedTargets > 0)
            logger.LogWarning("Ai modification {ConversationId}: se descartaron {Count} ids que no pertenecen al itinerario vigente.", conversation.Id, discardedTargets);

        List<AiItineraryItem> removed;
        var needsReplacement = true;
        decimal? priceCeiling = null;
        string? priceCeilingCurrency = null;

        switch (intent.Action)
        {
            case ModificationAction.REMOVE:
                removed = targeted;
                needsReplacement = false;
                break;

            case ModificationAction.REPLACE:
                removed = targeted;
                break;

            case ModificationAction.ADD:
                removed = [];
                break;

            case ModificationAction.REDUCE_BUDGET:
                // "Quiero gastar menos" sin monto: se saca el componente más caro y se exige que el
                // reemplazo sea estrictamente más barato EN LA MISMA MONEDA (sección 10: sin FX inventado).
                var mostExpensive = current.Items.OrderByDescending(i => i.EstimatedUnitPrice).FirstOrDefault();
                removed = mostExpensive is null ? [] : [mostExpensive];
                priceCeiling = mostExpensive?.EstimatedUnitPrice;
                priceCeilingCurrency = mostExpensive?.Currency;
                break;

            case ModificationAction.PREFER_PACKAGE:
                // El turista quiere un paquete en vez de experiencias sueltas: se rearma desde cero para
                // que el retrieval pueda proponer un Package que cubra varios días.
                removed = [.. current.Items];
                break;

            default:
                return null;
        }

        var removedIds = removed.Select(r => r.Id).ToHashSet();
        var preserved = current.Items.Where(i => !removedIds.Contains(i.Id)).ToList();

        // No volver a ofrecer lo que el turista sacó, ni duplicar lo que ya está preservado.
        var excluded = removed.Concat(preserved)
            .Select(i => i.ExperienceId ?? i.PackageId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        // En PREFER_PACKAGE los productos removidos SÍ pueden volver (dentro de un paquete), así que no
        // se excluyen: lo que cambia es la forma del itinerario, no el catálogo aceptable.
        if (intent.Action == ModificationAction.PREFER_PACKAGE)
            excluded.Clear();

        logger.LogInformation(
            "Ai modification {ConversationId}: acción={Action}, {Removed} ítem(s) fuera, {Preserved} preservado(s).",
            conversation.Id, intent.Action, removed.Count, preserved.Count);

        return new IterationPlan(current, intent.Action, preserved, removed, excluded, needsReplacement, priceCeiling, priceCeilingCurrency);
    }

    /// <summary>Qué conservar y qué rehacer en una iteración — lo decide el backend, no el LLM.</summary>
    private sealed record IterationPlan(
        AiItinerary Current,
        ModificationAction Action,
        IReadOnlyList<AiItineraryItem> PreservedItems,
        IReadOnlyList<AiItineraryItem> RemovedItems,
        IReadOnlyCollection<Guid> ExcludedProductIds,
        bool NeedsReplacement,
        decimal? PriceCeilingPerPerson,
        string? PriceCeilingCurrency);

    public async Task<ItineraryResponse> GetLatestItineraryAsync(Guid conversationId, CancellationToken ct)
    {
        var conversation = await conversationRepository.GetByIdForReadAsync(conversationId, ct)
            ?? throw new NotFoundAppException("Conversación no encontrada.");
        EnsureOwnsConversation(conversation.TouristId);

        var itinerary = await itineraryRepository.GetLatestByConversationIdAsync(conversationId, ct)
            ?? throw new NotFoundAppException("Todavía no se generó ningún itinerario para esta conversación.");

        // La propuesta vigente puede tener horas o semanas: se revalida igual que al retomar (sección 9).
        var revalidation = await revalidationService.RevalidateAsync(itinerary, ct);

        return AiItineraryMapper.ToResponse(itinerary, conversation.TravelersCount ?? 1, revalidation);
    }

    // ---- UC-AI-02/03/04/05: retrieval + composición + validación anti-hallucination ----

    private async Task<SendMessageResponse> GenerateItineraryAsync(
        AiConversation conversation, List<ConversationTurn> history, string userMessage, DateTimeOffset now,
        IterationPlan? plan, CancellationToken ct)
    {
        var tripDurationDays = conversation.DurationDays
            ?? (conversation.StartDate.HasValue && conversation.EndDate.HasValue
                ? conversation.EndDate.Value.DayNumber - conversation.StartDate.Value.DayNumber + 1
                : 1);
        tripDurationDays = Math.Max(tripDurationDays, 1);

        var travelers = conversation.TravelersCount ?? 1;
        var budgetPerPerson = conversation.BudgetTotal.HasValue ? conversation.BudgetTotal.Value / travelers : (decimal?)null;

        var warnings = new List<string>();

        // UC-AI-05 / sección 3: los ítems que se conservan NO se arrastran a ciegas — se revalidan
        // contra Postgres igual que si fueran nuevos, y el que dejó de ser válido se cae con aviso.
        var preserved = new List<AiItineraryItem>();
        var needsReplacement = plan?.NeedsReplacement ?? false;

        if (plan is not null && plan.PreservedItems.Count > 0)
        {
            var (stillValid, lostWarnings, lostAny) = await RevalidatePreservedAsync(plan, ct);
            preserved.AddRange(stillValid);
            warnings.AddRange(lostWarnings);
            if (lostAny) needsReplacement = true;
        }

        // Un REMOVE puro no necesita candidatos nuevos: se persiste lo que quedó, sin volver a buscar
        // catálogo (sección 1 — iterar no es rehacer la búsqueda desde cero).
        if (plan is not null && !needsReplacement)
            return await PersistIterationAsync(conversation, plan, preserved, [], warnings, userMessage, now, travelers, ct);

        var retrieval = await retrievalService.RetrieveAsync(
            new RetrievalQuery(
                conversation.PreferredDestinationId,
                conversation.StartDate,
                conversation.EndDate,
                tripDurationDays,
                budgetPerPerson,
                conversation.BudgetCurrency,
                conversation.Categories.Select(c => c.Id).ToList(),
                plan?.ExcludedProductIds ?? []),
            ct);

        // "Quiero gastar menos": solo sirven candidatos estrictamente más baratos en LA MISMA moneda —
        // comparar contra otra moneda exigiría una conversión que no hacemos (sección 10).
        if (plan is { PriceCeilingPerPerson: { } ceiling, PriceCeilingCurrency: { } ceilingCurrency })
        {
            retrieval = new RetrievalResult(
                [.. retrieval.Experiences.Where(e => IsCheaperInSameCurrency(e.Price, e.Currency, ceiling, ceilingCurrency))],
                [.. retrieval.Packages.Where(p => IsCheaperInSameCurrency(p.Price, p.Currency, ceiling, ceilingCurrency))]);

            if (retrieval.Experiences.Count == 0 && retrieval.Packages.Count == 0)
                warnings.Add($"No encontré alternativas más baratas que {ceilingCurrency} {ceiling:0.##} en la misma moneda.");
        }

        logger.LogInformation(
            "Ai retrieval {ConversationId}: {ExperienceCount} experiencias, {PackageCount} paquetes candidatos (iteración={IsIteration}).",
            conversation.Id, retrieval.Experiences.Count, retrieval.Packages.Count, plan is not null);

        if (retrieval.Experiences.Count == 0 && retrieval.Packages.Count == 0)
        {
            // Si estábamos iterando y todavía quedan ítems válidos, la propuesta no se pierde por no
            // haber encontrado reemplazo: se persiste lo que sobrevivió.
            if (plan is not null && preserved.Count > 0)
            {
                warnings.Add("No encontré alternativas nuevas, así que mantuve el resto de tu itinerario.");
                return await PersistIterationAsync(conversation, plan, preserved, [], warnings, userMessage, now, travelers, ct);
            }

            const string noMatchesReply = "No encontré experiencias ni paquetes publicados que coincidan con lo que buscás todavía. ¿Querés ajustar el destino, las fechas o el presupuesto?";
            AddMessage(conversation.Id, MessageSender.AI, noMatchesReply, now);
            await db.SaveChangesAsync(ct);

            return new SendMessageResponse
            {
                AssistantMessage = noMatchesReply,
                ParsedPreferences = ToPreferencesResponse(conversation),
                ClarificationNeeded = false,
                // Los avisos acumulados (ej. un ítem preservado que se cayó) NO se pisan: son justamente
                // lo que explica por qué el itinerario quedó como quedó.
                Warnings = [.. warnings, "No hay candidatos publicados disponibles para estos criterios."]
            };
        }

        ItineraryCompositionResult composition;
        try
        {
            composition = await aiModelClient.ComposeItineraryAsync(
                new ItineraryCompositionRequest(
                    BuildSnapshot(conversation), tripDurationDays, retrieval.Experiences, retrieval.Packages,
                    preserved.Select(ToPreservedItem).ToList(),
                    plan is null ? null : userMessage),
                ct);
        }
        catch (Exception ex) when (ex is AiModelUnavailableException or AiModelResponseException)
        {
            logger.LogWarning(ex, "SendMessage {ConversationId}: no se pudo componer el itinerario.", conversation.Id);
            return await FailGracefullyAsync(conversation, now, ct);
        }

        var (validItems, compositionWarnings) = ValidateComposedItems(composition.Items, retrieval);
        warnings.AddRange(compositionWarnings);

        // El modelo no puede meter mano en los días que el turista NO pidió tocar: si propone algo para
        // un día ocupado por un ítem preservado, se descarta. La excepción es ADD, donde sumar algo a un
        // día que ya tiene actividades es justamente lo que se pidió.
        if (plan is not null && plan.Action != ModificationAction.ADD && preserved.Count > 0)
        {
            var preservedDays = preserved.Select(p => p.DayNumber).ToHashSet();
            var intruders = validItems.Where(i => preservedDays.Contains(i.DayNumber)).ToList();
            if (intruders.Count > 0)
            {
                logger.LogWarning(
                    "Ai composition {ConversationId}: se descartaron {Count} ítem(s) que el modelo propuso para días preservados.",
                    conversation.Id, intruders.Count);
                validItems = [.. validItems.Except(intruders)];
            }
        }

        logger.LogInformation(
            "Ai composition {ConversationId}: {Selected} ítems elegidos por el modelo, {Valid} válidos tras la revalidación.",
            conversation.Id, composition.Items.Count, validItems.Count);

        // En una iteración, lo preservado ya es una propuesta válida por sí solo: que el modelo no
        // encuentre reemplazo no puede borrar el itinerario del turista.
        if (validItems.Count == 0 && plan is not null && preserved.Count > 0)
        {
            warnings.Add("No encontré un reemplazo válido, así que mantuve el resto de tu itinerario tal como estaba.");
            return await PersistIterationAsync(conversation, plan, preserved, [], warnings, userMessage, now, travelers, ct);
        }

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

        var explanation = string.IsNullOrWhiteSpace(composition.AssistantExplanation)
            ? "Armé una propuesta de itinerario con productos reales disponibles."
            : composition.AssistantExplanation;

        return await PersistProposalAsync(
            conversation, plan, preserved, validItems, warnings,
            string.IsNullOrWhiteSpace(composition.Title) ? null : composition.Title,
            explanation, now, travelers, ct);
    }

    /// <summary>
    /// Persiste una iteración sin ítems nuevos (REMOVE puro, o no hubo reemplazo válido): se conserva lo
    /// que quedaba y se sube la versión, para que el historial refleje el ajuste igual.
    /// </summary>
    private Task<SendMessageResponse> PersistIterationAsync(
        AiConversation conversation, IterationPlan plan, List<AiItineraryItem> preserved, List<AiItineraryItem> newItems,
        List<string> warnings, string userMessage, DateTimeOffset now, int travelers, CancellationToken ct)
    {
        var explanation = plan.Action == ModificationAction.REMOVE
            ? "Listo, saqué eso del itinerario y dejé el resto como estaba."
            : "Actualicé tu itinerario con el ajuste que pediste.";

        return PersistProposalAsync(conversation, plan, preserved, newItems, warnings, plan.Current.Title, explanation, now, travelers, ct);
    }

    /// <summary>
    /// Persiste la propuesta como una fila NUEVA de AiItinerary. En una iteración la versión se
    /// incrementa (domain-model.md: "Version se incrementa en cada ajuste (UC-AI-05)") y la propuesta
    /// anterior se conserva intacta — así queda la trazabilidad de cómo evolucionó la conversación sin
    /// necesidad de tablas de versionado extra.
    /// </summary>
    private async Task<SendMessageResponse> PersistProposalAsync(
        AiConversation conversation, IterationPlan? plan, List<AiItineraryItem> preserved, List<AiItineraryItem> newItems,
        List<string> warnings, string? title, string explanation, DateTimeOffset now, int travelers, CancellationToken ct)
    {
        var itinerary = new AiItinerary
        {
            Id = Guid.NewGuid(),
            AiConversationId = conversation.Id,
            TouristId = conversation.TouristId,
            Title = title,
            Status = AiItineraryStatus.DRAFT,
            Version = plan is null ? 1 : plan.Current.Version + 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Preservados primero: mantienen su día original, y los nuevos se acomodan detrás dentro del día.
        var sortOrderByDay = new Dictionary<int, int>();
        foreach (var item in preserved.Concat(newItems).OrderBy(i => i.DayNumber))
        {
            item.SortOrder = sortOrderByDay.GetValueOrDefault(item.DayNumber);
            sortOrderByDay[item.DayNumber] = item.SortOrder + 1;
            itinerary.Items.Add(item);
        }

        if (itinerary.Items.Count == 0)
        {
            const string emptyReply = "Con ese ajuste no queda ningún componente en el itinerario. ¿Querés que busque otras opciones?";
            AddMessage(conversation.Id, MessageSender.AI, emptyReply, now);
            await db.SaveChangesAsync(ct);

            return new SendMessageResponse
            {
                AssistantMessage = emptyReply,
                ParsedPreferences = ToPreferencesResponse(conversation),
                ClarificationNeeded = false,
                Warnings = warnings
            };
        }

        await itineraryRepository.AddAsync(itinerary, ct);
        AddMessage(conversation.Id, MessageSender.AI, explanation, now);
        await db.SaveChangesAsync(ct);

        var persisted = await itineraryRepository.GetByIdForReadAsync(itinerary.Id, ct)
            ?? throw new InvalidOperationException("El itinerario recién creado no pudo leerse.");

        return new SendMessageResponse
        {
            AssistantMessage = explanation,
            ParsedPreferences = ToPreferencesResponse(conversation),
            ClarificationNeeded = false,
            // Recién persistido y validado en este mismo request: no hace falta revalidar de nuevo.
            Itinerary = AiItineraryMapper.ToResponse(persisted, travelers, ItineraryRevalidationResult.Empty),
            Warnings = warnings
        };
    }

    /// <summary>
    /// Sección 3 — los ítems preservados se revalidan contra Postgres antes de reinsertarlos, pero
    /// preservar significa preservar: se reinsertan con SU snapshot original (precio y moneda del
    /// momento en que se propusieron), no con el precio de hoy. Un cambio de precio se informa como
    /// warning y queda visible de forma permanente al leer el itinerario (el DTO expone snapshot y
    /// precio vigente lado a lado); reescribir el snapshot lo haría desaparecer para siempre.
    /// Solo se reemplaza lo que dejó de ser válido: eso sí se cae, con aviso.
    /// </summary>
    private async Task<(List<AiItineraryItem> Items, List<string> Warnings, bool LostAny)> RevalidatePreservedAsync(
        IterationPlan plan, CancellationToken ct)
    {
        var snapshot = new AiItinerary { Id = plan.Current.Id, Items = [.. plan.PreservedItems] };
        var revalidation = await revalidationService.RevalidateAsync(snapshot, ct);

        var kept = new List<AiItineraryItem>();
        var warnings = new List<string>();
        var lostAny = false;

        foreach (var item in plan.PreservedItems)
        {
            if (!revalidation.ByItemId.TryGetValue(item.Id, out var live) || !live.IsValid)
            {
                lostAny = true;
                warnings.AddRange(live?.Warnings ?? ["Un componente que ibas a conservar ya no está disponible."]);
                warnings.Add("Lo saqué del itinerario porque ya no se puede reservar.");
                continue;
            }

            if (live.PriceChanged(item.EstimatedUnitPrice, item.Currency))
                warnings.AddRange(live.Warnings);

            kept.Add(new AiItineraryItem
            {
                Id = Guid.NewGuid(),
                DayNumber = item.DayNumber,
                SortOrder = item.SortOrder,
                ProductType = item.ProductType,
                ExperienceId = item.ExperienceId,
                PackageId = item.PackageId,
                ExperienceAvailabilityId = item.ExperienceAvailabilityId,
                PackageAvailabilityId = item.PackageAvailabilityId,
                // Snapshot ORIGINAL: este ítem no se volvió a proponer, se conservó tal cual. El precio
                // vigente no se persiste acá — viaja por DTO (CurrentPrice) cada vez que se lee.
                EstimatedUnitPrice = item.EstimatedUnitPrice,
                Currency = item.Currency
            });
        }

        return (kept, warnings, lostAny);
    }

    private static bool IsCheaperInSameCurrency(decimal price, string currency, decimal ceiling, string ceilingCurrency) =>
        string.Equals(currency, ceilingCurrency, StringComparison.OrdinalIgnoreCase) && price < ceiling;

    private static PreservedItem ToPreservedItem(AiItineraryItem item) => new(
        item.DayNumber,
        item.ProductType.ToString(),
        item.ExperienceId ?? item.PackageId ?? Guid.Empty,
        item.Experience?.Title ?? item.Package?.Title ?? "Componente");

    private static CurrentItineraryItemView ToItemView(AiItineraryItem item) => new(
        item.Id,
        item.DayNumber,
        item.ProductType.ToString(),
        item.Experience?.Title ?? item.Package?.Title ?? "Componente",
        item.Experience?.Categories.Select(c => c.Name).ToList() ?? item.Package?.Categories.Select(c => c.Name).ToList() ?? [],
        item.EstimatedUnitPrice,
        item.Currency,
        item.ExperienceAvailability?.Date ?? item.PackageAvailability?.DepartureDate);

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

}
