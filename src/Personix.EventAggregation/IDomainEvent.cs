namespace Personix.EventAggregation;

/// <summary>
/// Contract every event published through <see cref="IEventAggregator"/> must implement. Implementing
/// types are typically simple, serializable records — <see cref="EventAggregator"/> passes them to
/// <see cref="System.Text.Json.JsonSerializer"/> as-is before persisting them.
/// </summary>
public interface IDomainEvent
{
    /// <summary>A unique identifier for this specific occurrence of the event.</summary>
    Guid EventId { get; }

    /// <summary>When the event occurred, in UTC.</summary>
    DateTime OccurredAtUtc { get; }

    /// <summary>
    /// A string identifier for the event, recorded when it is persisted and passed to the webhook
    /// dispatcher, the logger, and the trace activity name.
    /// </summary>
    /// <remarks>
    /// Independent of the event's CLR type: <see cref="EventAggregator.Subscribe{TEvent}"/> and
    /// <see cref="EventAggregator.PublishAsync{TEvent}"/> route in-memory handlers by
    /// <c>typeof(TEvent).Name</c>, not by this property. An implementation is free to make
    /// <see cref="EventType"/> whatever string makes sense for storage and webhooks — it does not have
    /// to match the CLR type name for in-memory handler dispatch to keep working.
    /// </remarks>
    string EventType { get; }
}
