using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Personix.Tracing;
using Shouldly;
using Xunit;

namespace Personix.EventAggregation.Tests;

/// <summary>
/// Covers the wiring between EventAggregator and Personix.Tracing: an activity is really started
/// for every publish, it carries the expected name and kind, and the traceparent it produces is
/// the exact one handed to IEventRepository.PersistAsync — a real ActivityListener observes this,
/// nothing about Activity/ActivitySource is mocked.
/// </summary>
public sealed class EventAggregatorTracingTests
{
    private const string SourceName = "Personix.EventAggregation.Tests.Tracing";
    private const string RecordedFlag = "01";

    private readonly RecordingEventRepository _repository = new();
    private readonly RecordingWebhookDispatcher _dispatcher = new();

    private EventAggregator CreateAggregator(IEventRepository? repository = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository ?? _repository);
        services.AddSingleton<IWebhookDispatcher>(_dispatcher);

        return new EventAggregator(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventAggregator>.Instance);
    }

    private static string TraceparentOf(Activity activity) => $"00-{activity.TraceId}-{activity.SpanId}-{RecordedFlag}";

    [Fact]
    public async Task PublishAsync_StartsAnActivityNamedAfterTheEventType()
    {
        ActivityHelper.Configure(SourceName);
        using var listener = new ListenerScope(SourceName);
        var aggregator = CreateAggregator();

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);

        var activity = listener.Started.ShouldHaveSingleItem();
        activity.OperationName.ShouldBe("EventAggregator.Publish test.event");
    }

    [Fact]
    public async Task PublishAsync_StartsTheActivityWithInternalKind()
    {
        ActivityHelper.Configure(SourceName);
        using var listener = new ListenerScope(SourceName);
        var aggregator = CreateAggregator();

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);

        listener.Started.ShouldHaveSingleItem().Kind.ShouldBe(ActivityKind.Internal);
    }

    [Fact]
    public async Task PublishAsync_StopsTheActivityBeforeReturning()
    {
        ActivityHelper.Configure(SourceName);
        using var listener = new ListenerScope(SourceName);
        var aggregator = CreateAggregator();

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);

        listener.Started.ShouldHaveSingleItem().IsStopped.ShouldBeTrue();
    }

    [Fact]
    public async Task PublishAsync_PersistsTheTraceparentOfTheActivityItStarted()
    {
        ActivityHelper.Configure(SourceName);
        using var listener = new ListenerScope(SourceName);
        var aggregator = CreateAggregator();

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);

        var activity = listener.Started.ShouldHaveSingleItem();
        _repository.Persisted.ShouldHaveSingleItem().Traceparent.ShouldBe(TraceparentOf(activity));
    }

    [Fact]
    public async Task PublishAsync_PersistsANullTraceparent_WhenNothingListensToTheActivitySource()
    {
        ActivityHelper.Configure(SourceName);
        // Deliberately no ListenerScope: StartActivity returns null when nothing is listening, so
        // publishing must still succeed and simply persist without a trace context.
        var aggregator = CreateAggregator();

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);

        _repository.Persisted.ShouldHaveSingleItem().Traceparent.ShouldBeNull();
    }

    [Fact]
    public async Task PublishAsync_KeepsTheActivityCurrent_WhileTheRepositoryPersists()
    {
        ActivityHelper.Configure(SourceName);
        using var listener = new ListenerScope(SourceName);
        Activity? currentDuringPersist = null;
        var repository = new CallbackEventRepository(() => currentDuringPersist = Activity.Current);
        var aggregator = CreateAggregator(repository);

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);

        var activity = listener.Started.ShouldHaveSingleItem();
        currentDuringPersist.ShouldBeSameAs(activity);
    }

    [Fact]
    public async Task PublishAsync_CreatesADistinctActivityPerCall_WithDistinctTraceparents()
    {
        ActivityHelper.Configure(SourceName);
        using var listener = new ListenerScope(SourceName);
        var aggregator = CreateAggregator();

        await aggregator.PublishAsync(new TestEvent("first"), CancellationToken.None);
        await aggregator.PublishAsync(new TestEvent("second"), CancellationToken.None);

        listener.Started.Count.ShouldBe(2);
        listener.Started[0].TraceId.ShouldNotBe(listener.Started[1].TraceId);
        _repository.Persisted[0].Traceparent.ShouldNotBe(_repository.Persisted[1].Traceparent);
    }
}
