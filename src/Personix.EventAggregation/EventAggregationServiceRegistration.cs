using Microsoft.Extensions.DependencyInjection;

namespace Personix.EventAggregation;

/// <summary>
/// Extension method for wiring this package's <see cref="IEventAggregator"/> into an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class EventAggregationServiceRegistration
{
    /// <summary>Registers <see cref="EventAggregator"/> as the singleton <see cref="IEventAggregator"/>.</summary>
    /// <param name="services">The container to register into.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// This call registers only the aggregator itself. <see cref="IEventRepository"/> and
    /// <see cref="IWebhookDispatcher"/> are NOT registered — the consumer must supply and register its
    /// own implementations of both, or the first call to
    /// <see cref="IEventAggregator.PublishAsync{TEvent}"/> fails with an
    /// <see cref="InvalidOperationException"/> when it tries to resolve them. Calling this method more
    /// than once registers a second, independent singleton rather than replacing the first: the last
    /// registration wins when a single <see cref="IEventAggregator"/> is resolved, and any earlier
    /// instance — along with its subscribers — becomes unreachable.
    /// </remarks>
    public static IServiceCollection AddEventAggregation(this IServiceCollection services)
    {
        services.AddSingleton<IEventAggregator, EventAggregator>();
        return services;
    }
}
