using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Personix.EventAggregation.Tests;

public sealed class EventAggregatorTests
{
    private readonly RecordingEventRepository _repository = new();
    private readonly RecordingWebhookDispatcher _dispatcher = new();

    private EventAggregator CreateAggregator(IWebhookDispatcher? dispatcher = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventRepository>(_repository);
        services.AddSingleton(dispatcher ?? _dispatcher);

        return new EventAggregator(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventAggregator>.Instance);
    }

    [Fact]
    public async Task PublishAsync_PersistsTheEvent()
    {
        var aggregator = CreateAggregator();
        var domainEvent = new TestEvent("first");

        await aggregator.PublishAsync(domainEvent, CancellationToken.None);

        var persisted = _repository.Persisted.ShouldHaveSingleItem();
        persisted.EventType.ShouldBe("test.event");
        persisted.EventId.ShouldBe(domainEvent.EventId.ToString());
        persisted.Payload.ShouldContain("first");
    }

    [Fact]
    public async Task PublishAsync_DispatchesTheEventToWebhooks()
    {
        var aggregator = CreateAggregator();
        var domainEvent = new TestEvent("first");

        await aggregator.PublishAsync(domainEvent, CancellationToken.None);

        _dispatcher.Dispatched.ShouldHaveSingleItem().ShouldBe(domainEvent);
    }

    [Fact]
    public async Task PublishAsync_InvokesSubscribedHandlers()
    {
        var aggregator = CreateAggregator();
        var received = new List<TestEvent>();
        aggregator.Subscribe<TestEvent>((e, _) =>
        {
            received.Add(e);
            return Task.CompletedTask;
        });

        var domainEvent = new TestEvent("first");
        await aggregator.PublishAsync(domainEvent, CancellationToken.None);

        received.ShouldHaveSingleItem().ShouldBe(domainEvent);
    }

    [Fact]
    public async Task PublishAsync_InvokesEveryHandlerForTheSameEventType()
    {
        var aggregator = CreateAggregator();
        var calls = 0;
        aggregator.Subscribe<TestEvent>((_, _) => { calls++; return Task.CompletedTask; });
        aggregator.Subscribe<TestEvent>((_, _) => { calls++; return Task.CompletedTask; });

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);

        calls.ShouldBe(2);
    }

    [Fact]
    public async Task PublishAsync_DoesNotInvokeHandlersRegisteredForOtherEventTypes()
    {
        var aggregator = CreateAggregator();
        var called = false;
        aggregator.Subscribe<OtherEvent>((_, _) => { called = true; return Task.CompletedTask; });

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);

        called.ShouldBeFalse();
    }

    [Fact]
    public async Task PublishAsync_SwallowsHandlerFailuresSoOneBadHandlerCannotBlockTheRest()
    {
        var aggregator = CreateAggregator();
        var secondHandlerRan = false;
        aggregator.Subscribe<TestEvent>((_, _) => throw new InvalidOperationException("handler blew up"));
        aggregator.Subscribe<TestEvent>((_, _) => { secondHandlerRan = true; return Task.CompletedTask; });

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);

        secondHandlerRan.ShouldBeTrue();
    }

    [Fact]
    public async Task PublishAsync_PropagatesDispatcherFailures()
    {
        var aggregator = CreateAggregator(new ThrowingWebhookDispatcher());

        await Should.ThrowAsync<InvalidOperationException>(
            () => aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_PersistsBeforeDispatching()
    {
        var aggregator = CreateAggregator(new ThrowingWebhookDispatcher());

        await Should.ThrowAsync<InvalidOperationException>(
            () => aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None));

        _repository.Persisted.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task PublishAsync_PropagatesRepositoryFailures()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventRepository>(new ThrowingEventRepository());
        services.AddSingleton<IWebhookDispatcher>(_dispatcher);
        var aggregator = new EventAggregator(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventAggregator>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(
            () => aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_DoesNotDispatchToWebhooks_WhenPersistingFails()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventRepository>(new ThrowingEventRepository());
        services.AddSingleton<IWebhookDispatcher>(_dispatcher);
        var aggregator = new EventAggregator(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventAggregator>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(
            () => aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None));

        _dispatcher.Dispatched.ShouldBeEmpty();
    }

    [Fact]
    public async Task PublishAsync_DoesNotInvokeHandlers_WhenDispatchingFails()
    {
        var aggregator = CreateAggregator(new ThrowingWebhookDispatcher());
        var handlerCalled = false;
        aggregator.Subscribe<TestEvent>((_, _) => { handlerCalled = true; return Task.CompletedTask; });

        await Should.ThrowAsync<InvalidOperationException>(
            () => aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None));

        handlerCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task PublishAsync_ToleratesAHandlerThatSubscribesWhileItRuns()
    {
        var aggregator = CreateAggregator();
        aggregator.Subscribe<TestEvent>((_, _) =>
        {
            aggregator.Subscribe<TestEvent>((_, _) => Task.CompletedTask);
            return Task.CompletedTask;
        });

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);
    }

    [Fact]
    public async Task PublishAsync_DoesNotInvokeAHandlerThatSubscribesDuringTheSamePublication()
    {
        // Regression test: _handlers used to be a ConcurrentDictionary<string, List<...>>. The
        // dictionary itself was thread-safe, but the List value inside it was not — a handler
        // subscribing mid-publish mutated the very list the foreach below was enumerating and blew
        // up with "Collection was modified". Switching the value to ImmutableList<T> fixed it: Add
        // returns a new list instead of mutating the one currently being enumerated, so the handler
        // added during this publish simply is not part of the snapshot this loop is iterating.
        var aggregator = CreateAggregator();
        var lateHandlerCalls = 0;
        aggregator.Subscribe<TestEvent>((_, _) =>
        {
            aggregator.Subscribe<TestEvent>((_, _) => { lateHandlerCalls++; return Task.CompletedTask; });
            return Task.CompletedTask;
        });

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);
        lateHandlerCalls.ShouldBe(0);

        await aggregator.PublishAsync(new TestEvent("second"), CancellationToken.None);
        lateHandlerCalls.ShouldBe(1);
    }
}
