using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Personix.EventAggregation.Tests;

/// <summary>
/// Covers the package's entry point. AddEventAggregation deliberately registers only the
/// aggregator itself — IEventRepository and IWebhookDispatcher are the consumer's job — so these
/// tests pin both what IS wired (the aggregator, as a singleton) and what is deliberately left for
/// the consumer to supply, plus what happens on a second registration call.
/// </summary>
public sealed class EventAggregationServiceRegistrationTests
{
    // EventAggregator's constructor needs ILogger<EventAggregator>. Registering it directly (rather
    // than pulling in Microsoft.Extensions.Logging's AddLogging) keeps these tests focused on what
    // AddEventAggregation itself contributes to the container.
    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<EventAggregator>>(NullLogger<EventAggregator>.Instance);
        return services;
    }

    [Fact]
    public void AddEventAggregation_ReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddEventAggregation();

        returned.ShouldBeSameAs(services);
    }

    [Fact]
    public void AddEventAggregation_RegistersIEventAggregator_ResolvableFromContainer()
    {
        var services = CreateServices();
        services.AddEventAggregation();
        using var provider = services.BuildServiceProvider();

        var aggregator = provider.GetRequiredService<IEventAggregator>();

        aggregator.ShouldBeOfType<EventAggregator>();
    }

    [Fact]
    public void AddEventAggregation_RegistersIEventAggregator_AsASingleton()
    {
        var services = CreateServices();
        services.AddEventAggregation();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IEventAggregator>();
        var second = provider.GetRequiredService<IEventAggregator>();

        first.ShouldBeSameAs(second);
    }

    [Fact]
    public void AddEventAggregation_SingletonInstance_IsSharedAcrossScopes()
    {
        // In-memory Subscribe/PublishAsync only make sense if every scope (every request, every
        // background job) reaches the same aggregator instance. If this were Scoped, subscriptions
        // registered in one scope would be invisible to a publish from another.
        var services = CreateServices();
        services.AddEventAggregation();
        using var provider = services.BuildServiceProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var fromScopeA = scopeA.ServiceProvider.GetRequiredService<IEventAggregator>();
        var fromScopeB = scopeB.ServiceProvider.GetRequiredService<IEventAggregator>();

        fromScopeA.ShouldBeSameAs(fromScopeB);
    }

    [Fact]
    public void AddEventAggregation_DoesNotRegisterItsCollaborators()
    {
        var services = CreateServices();

        services.AddEventAggregation();
        using var provider = services.BuildServiceProvider();

        provider.GetService<IEventRepository>().ShouldBeNull();
        provider.GetService<IWebhookDispatcher>().ShouldBeNull();
    }

    [Fact]
    public async Task PublishAsync_ThrowsInvalidOperationException_WhenCollaboratorsAreNotRegistered()
    {
        // Today's behaviour: registration succeeds silently either way, and a missing collaborator
        // only surfaces the first time something publishes, as an InvalidOperationException from
        // the service provider. Documented here as-is — whether AddEventAggregation should instead
        // fail fast at startup when IEventRepository/IWebhookDispatcher are missing is an open
        // product question, not something to decide from a test.
        var services = CreateServices();
        services.AddEventAggregation();
        using var provider = services.BuildServiceProvider();
        var aggregator = provider.GetRequiredService<IEventAggregator>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None));
    }

    [Fact]
    public void AddEventAggregation_CalledTwice_RegistersASecondIndependentSingletonInstance()
    {
        var services = CreateServices();

        services.AddEventAggregation();
        services.AddEventAggregation();
        using var provider = services.BuildServiceProvider();

        var all = provider.GetServices<IEventAggregator>().ToList();

        all.Count.ShouldBe(2);
        all[0].ShouldNotBeSameAs(all[1]);
    }

    [Fact]
    public void AddEventAggregation_CalledTwice_ResolvingASingleInstanceReturnsTheLastRegistration()
    {
        // AddSingleton never checks for an existing registration, so calling AddEventAggregation
        // twice does not throw and does not dedupe — it appends a second descriptor. Microsoft.
        // Extensions.DependencyInjection resolves a single (non-IEnumerable) dependency to the LAST
        // registered descriptor, so GetRequiredService quietly returns the second instance and the
        // first is orphaned. A consumer who calls AddEventAggregation() twice by accident (e.g. from
        // two composition roots) gets two live aggregators with independent subscriber lists.
        var services = CreateServices();

        services.AddEventAggregation();
        services.AddEventAggregation();
        using var provider = services.BuildServiceProvider();

        var all = provider.GetServices<IEventAggregator>().ToList();
        var resolved = provider.GetRequiredService<IEventAggregator>();

        resolved.ShouldBeSameAs(all[^1]);
    }
}
