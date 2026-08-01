
namespace Personix.EventAggregation.Tests;

internal sealed record TestEvent(string Name) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; } = DateTime.UtcNow;
    public string EventType => "test.event";
}

internal sealed record OtherEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; } = DateTime.UtcNow;
    public string EventType => "other.event";
}

internal sealed record PersistedEvent(string EventType, string EventId, string Payload, string? Traceparent);

internal sealed class RecordingEventRepository : IEventRepository
{
    internal List<PersistedEvent> Persisted { get; } = [];

    public Task PersistAsync(
        string eventType,
        string eventId,
        string payload,
        string? traceparent,
        CancellationToken cancellationToken)
    {
        Persisted.Add(new PersistedEvent(eventType, eventId, payload, traceparent));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string EventType, string Payload, DateTime OccurredAt, string? Traceparent)>>
        GetRecentAsync(int count, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<(string, string, DateTime, string?)>>([]);
}

internal sealed class RecordingWebhookDispatcher : IWebhookDispatcher
{
    internal List<IDomainEvent> Dispatched { get; } = [];

    public Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        Dispatched.Add(domainEvent);
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingWebhookDispatcher : IWebhookDispatcher
{
    public Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
        => throw new InvalidOperationException("webhook endpoint unreachable");
}

internal sealed class ThrowingEventRepository : IEventRepository
{
    public Task PersistAsync(
        string eventType,
        string eventId,
        string payload,
        string? traceparent,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("database unreachable");

    public Task<IReadOnlyList<(string EventType, string Payload, DateTime OccurredAt, string? Traceparent)>>
        GetRecentAsync(int count, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<(string, string, DateTime, string?)>>([]);
}

/// <summary>
/// Records persisted events like <see cref="RecordingEventRepository"/>, but also runs a callback
/// from inside <see cref="PersistAsync"/> so a test can observe ambient state — such as the current
/// <see cref="System.Diagnostics.Activity"/> — at the exact moment persistence happens.
/// </summary>
internal sealed class CallbackEventRepository(Action onPersisting) : IEventRepository
{
    internal List<PersistedEvent> Persisted { get; } = [];

    public Task PersistAsync(
        string eventType,
        string eventId,
        string payload,
        string? traceparent,
        CancellationToken cancellationToken)
    {
        onPersisting();
        Persisted.Add(new PersistedEvent(eventType, eventId, payload, traceparent));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string EventType, string Payload, DateTime OccurredAt, string? Traceparent)>>
        GetRecentAsync(int count, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<(string, string, DateTime, string?)>>([]);
}
