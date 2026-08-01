namespace Personix.EventAggregation;

/// <summary>
/// Delivery contract <see cref="EventAggregator"/> uses to notify webhook subscribers about a
/// published event. This package ships no implementation, and
/// <see cref="EventAggregationServiceRegistration.AddEventAggregation"/> does not register one — the
/// consumer must add an <see cref="IWebhookDispatcher"/> to the container, or
/// <see cref="IEventAggregator.PublishAsync{TEvent}"/> throws an <see cref="InvalidOperationException"/>
/// the first time it runs.
/// </summary>
public interface IWebhookDispatcher
{
    /// <summary>Notifies this event's webhook subscribers about <paramref name="domainEvent"/>.</summary>
    /// <param name="domainEvent">The event to deliver.</param>
    /// <param name="cancellationToken">Cancellation for the dispatch operation.</param>
    /// <typeparam name="TEvent">The event's static type.</typeparam>
    /// <remarks>
    /// Runs after the event has been persisted and before its in-memory handlers — see
    /// <see cref="EventAggregator.PublishAsync{TEvent}"/> for the full ordering and what happens when
    /// this method throws.
    /// </remarks>
    Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent;
}
