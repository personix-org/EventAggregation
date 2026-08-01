using System.Diagnostics;

namespace Personix.EventAggregation.Tests;

/// <summary>
/// Registers an <see cref="ActivityListener"/> for a single activity source so that
/// <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> actually produces an activity,
/// and records every activity it starts so a test can assert on the real <see cref="Activity"/>
/// instance instead of mocking tracing types. Without a listener the runtime samples everything
/// out and StartActivity returns null.
/// </summary>
internal sealed class ListenerScope : IDisposable
{
    private readonly ActivityListener _listener;

    internal List<Activity> Started { get; } = [];

    internal ListenerScope(string activitySourceName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == activitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => Started.Add(activity),
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();
}
