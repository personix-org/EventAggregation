namespace Personix.EventAggregation;

/// <summary>
/// Publishes domain events and lets in-process code subscribe to them. The only implementation this
/// package ships is <see cref="EventAggregator"/> — see
/// <see cref="EventAggregationServiceRegistration.AddEventAggregation"/> for exactly what registering
/// it does, and does not, wire up for you.
/// </summary>
public interface IEventAggregator
{
    /// <summary>
    /// Publishes <paramref name="domainEvent"/>: persists it, dispatches it to webhook subscribers,
    /// then invokes every handler registered for <typeparamref name="TEvent"/> via
    /// <see cref="Subscribe{TEvent}"/>.
    /// </summary>
    /// <param name="domainEvent">The event to publish.</param>
    /// <param name="cancellationToken">Propagated to persistence, the webhook dispatcher, and every handler.</param>
    /// <typeparam name="TEvent">The event's static type. Determines which handlers run.</typeparam>
    /// <remarks>
    /// See <see cref="EventAggregator.PublishAsync{TEvent}"/> for the shipped implementation's exact
    /// ordering and failure semantics.
    /// </remarks>
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent;

    /// <summary>
    /// Registers <paramref name="handler"/> to run whenever <see cref="PublishAsync{TEvent}"/>
    /// publishes a <typeparamref name="TEvent"/>.
    /// </summary>
    /// <param name="handler">
    /// The callback to invoke. Subscribing more than once for the same <typeparamref name="TEvent"/>
    /// adds handlers rather than replacing them — all of them run on every matching publish.
    /// </param>
    /// <typeparam name="TEvent">The event type this handler is interested in.</typeparam>
    /// <remarks>
    /// See <see cref="EventAggregator.Subscribe{TEvent}"/> for how the shipped implementation stores
    /// handlers and what happens when one of them throws.
    /// </remarks>
    void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : IDomainEvent;
}
