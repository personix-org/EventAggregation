namespace Personix.EventAggregation;

/// <summary>
/// Storage contract <see cref="EventAggregator"/> uses to persist every event before dispatching it.
/// This package ships no implementation, and
/// <see cref="EventAggregationServiceRegistration.AddEventAggregation"/> does not register one — the
/// consumer must add an <see cref="IEventRepository"/> to the container, or
/// <see cref="IEventAggregator.PublishAsync{TEvent}"/> throws an <see cref="InvalidOperationException"/>
/// the first time it runs.
/// </summary>
public interface IEventRepository
{
    /// <summary>Persists one published event.</summary>
    /// <param name="eventType">The event's <see cref="IDomainEvent.EventType"/>.</param>
    /// <param name="eventId">The event's <see cref="IDomainEvent.EventId"/>, as a string.</param>
    /// <param name="payload">The event, serialized to JSON.</param>
    /// <param name="traceparent">
    /// The W3C <c>traceparent</c> of the activity that published the event, or <see langword="null"/>
    /// when nothing was listening for traces at the time. Storing it lets the persisted record later
    /// be correlated back to the request or job that raised the event.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the persistence operation.</param>
    Task PersistAsync(string eventType, string eventId, string payload, string? traceparent, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves recently persisted events. This package has no implementation to define "recent" or
    /// the result order — both are up to whichever <see cref="IEventRepository"/> the consumer
    /// registers.
    /// </summary>
    /// <param name="count">The maximum number of events to return.</param>
    /// <param name="cancellationToken">Cancellation for the read operation.</param>
    /// <returns>
    /// At most <paramref name="count"/> persisted events, each with its type, JSON payload, UTC
    /// occurrence time, and the <c>traceparent</c> it was published under, if any.
    /// </returns>
    Task<IReadOnlyList<(string EventType, string Payload, DateTime OccurredAt, string? Traceparent)>> GetRecentAsync(int count, CancellationToken cancellationToken);
}
