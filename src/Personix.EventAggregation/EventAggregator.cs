using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Personix.Tracing;

namespace Personix.EventAggregation;

/// <summary>
/// Default <see cref="IEventAggregator"/> shipped with this package: publishing an event persists it,
/// dispatches it to webhook subscribers, then runs the in-process handlers registered via
/// <see cref="Subscribe{TEvent}"/>.
/// </summary>
/// <param name="scopeFactory">
/// Creates a fresh DI scope for each call to <see cref="PublishAsync{TEvent}"/>, from which
/// <see cref="IEventRepository"/> and <see cref="IWebhookDispatcher"/> are resolved — whatever
/// lifetime the consumer registered them with.
/// </param>
/// <param name="logger">Receives one informational entry per published event, and one error entry per failing in-memory handler.</param>
/// <remarks>
/// Registered as a singleton by <see cref="EventAggregationServiceRegistration.AddEventAggregation"/>,
/// which does NOT register <see cref="IEventRepository"/> or <see cref="IWebhookDispatcher"/> — the
/// consumer must supply both, or <see cref="PublishAsync{TEvent}"/> fails the first time it tries to
/// resolve them.
/// </remarks>
public sealed class EventAggregator(
    IServiceScopeFactory scopeFactory,
    ILogger<EventAggregator> logger) : IEventAggregator
{
    // Immutable lists so that a handler subscribing during publication — or a subscription on
    // another thread — cannot mutate the sequence being enumerated.
    private readonly ConcurrentDictionary<string, ImmutableList<Func<object, CancellationToken, Task>>> _handlers = new();

    /// <summary>
    /// Persists <paramref name="domainEvent"/>, dispatches it to webhook subscribers, then invokes
    /// every handler registered for <typeparamref name="TEvent"/> — in that order.
    /// </summary>
    /// <param name="domainEvent">The event to publish.</param>
    /// <param name="cancellationToken">Passed to the repository, the webhook dispatcher, and every handler.</param>
    /// <typeparam name="TEvent">The event's static type. Selects which handlers (see <see cref="Subscribe{TEvent}"/>) run.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// <see cref="IEventRepository"/> or <see cref="IWebhookDispatcher"/> is not registered in the
    /// container — see <see cref="EventAggregationServiceRegistration.AddEventAggregation"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The three steps are not equally safe to fail. An exception while persisting or while
    /// dispatching webhooks propagates to the caller unwrapped, and every step after it is skipped.
    /// An exception from an individual handler is caught and logged instead — it does not fault this
    /// method, and the remaining handlers still run.
    /// </para>
    /// <para>
    /// One consequence is worth knowing: because persistence runs first, an event that fails at the
    /// webhook step has still been stored successfully. It is discoverable in
    /// <see cref="IEventRepository"/> and can be resent — the exception the caller sees means delivery
    /// failed, not that the event was lost.
    /// </para>
    /// </remarks>
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        var eventType = domainEvent.EventType;
        logger.LogInformation("Publishing event {EventType} ({EventId})", eventType, domainEvent.EventId);

        using var activity = ActivityHelper.StartActivity($"EventAggregator.Publish {eventType}");
        var traceparent = ActivityHelper.GetCurrentTraceparent();

        await using var scope = scopeFactory.CreateAsyncScope();

        var eventRepository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
        var payload = JsonSerializer.Serialize(domainEvent);
        await eventRepository.PersistAsync(eventType, domainEvent.EventId.ToString(), payload, traceparent, cancellationToken);

        var dispatcher = scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();
        await dispatcher.DispatchAsync(domainEvent, cancellationToken);

        var handlerKey = typeof(TEvent).Name;
        if (_handlers.TryGetValue(handlerKey, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    await handler(domainEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "In-memory handler for {EventType} threw an exception.", eventType);
                }
            }
        }
    }

    /// <summary>
    /// Registers <paramref name="handler"/> to run whenever <see cref="PublishAsync{TEvent}"/>
    /// publishes a <typeparamref name="TEvent"/>.
    /// </summary>
    /// <param name="handler">
    /// The callback to invoke. Handlers accumulate — subscribing more than once for the same
    /// <typeparamref name="TEvent"/> adds handlers rather than replacing them — and all of them run on
    /// every matching publish, in the order they were subscribed.
    /// </param>
    /// <typeparam name="TEvent">The event type this handler is interested in.</typeparam>
    /// <remarks>
    /// Thread-safe: <see cref="PublishAsync{TEvent}"/> enumerates a snapshot of the handler list, so a
    /// call to this method — whether from another thread or from inside a running handler — cannot
    /// mutate the sequence a publish is currently iterating.
    /// </remarks>
    public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : IDomainEvent
    {
        var eventType = typeof(TEvent).Name;
        var wrapped = WrapHandler(handler);

        _handlers.AddOrUpdate(
            eventType,
            _ => [wrapped],
            (_, existing) => existing.Add(wrapped));
    }

    private static Func<object, CancellationToken, Task> WrapHandler<TEvent>(
        Func<TEvent, CancellationToken, Task> handler)
        => (e, ct) => handler((TEvent)e, ct);
}
