namespace TurisClick.Api.Modules.Ai.Entities;

/// <summary>docs/domain-model.md §8. Soporta UC-T-13/15 (historial conversacional).</summary>
public class AiMessage
{
    public Guid Id { get; set; }

    public Guid AiConversationId { get; set; }
    public AiConversation? AiConversation { get; set; }

    public MessageSender Sender { get; set; }
    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
