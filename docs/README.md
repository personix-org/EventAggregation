# Personix.EventAggregation

In-process domain event aggregator. Publishing an event persists it, hands it to webhook
subscribers, and invokes in-memory handlers — with the W3C trace context stored alongside the
record, so an event can later be correlated back to the request that caused it.

The package deliberately exposes **no HTTP endpoints**. Events travel inside the process; webhooks
are the only thing that leaves it.

## Contents

| Type | Role |
|---|---|
| `IDomainEvent` | Marker for domain events — id, timestamp, and event type. |
| `IEventAggregator` | Publish events and subscribe in-memory handlers. |
| `IEventRepository` | Durable storage for published events. **You implement this.** |
| `IWebhookDispatcher` | Delivery of events to webhook subscribers. **You implement this.** |
| `EventAggregator` | The supplied implementation of `IEventAggregator`. |

## Installation

```xml
<PackageReference Include="Personix.EventAggregation" Version="1.0.0" />
```

## Usage

### 1. Define an event

```csharp
using Personix.EventAggregation;

public sealed record SubscriptionCreated(Guid SubscriptionId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => "subscription.created";
}
```

### 2. Register

```csharp
using Personix.EventAggregation;

builder.Services.AddEventAggregation();
```

`AddEventAggregation()` registers **only** the aggregator. The two collaborators are yours to
provide — publishing throws `InvalidOperationException` from the service provider without them:

```csharp
builder.Services.AddScoped<IEventRepository, SqliteEventRepository>();
builder.Services.AddScoped<IWebhookDispatcher, HttpWebhookDispatcher>();
```

Both are resolved from a **scope created per publish**, so scoped dependencies such as a
`DbContext` work as expected even when publishing from a singleton or a background service.

### 3. Publish

```csharp
public sealed class SubscriptionService(IEventAggregator events)
{
    public async Task CreateAsync(Guid id, CancellationToken ct)
    {
        // ... do the work ...
        await events.PublishAsync(new SubscriptionCreated(id), ct);
    }
}
```

### 4. Subscribe in-memory

```csharp
aggregator.Subscribe<SubscriptionCreated>(async (e, ct) =>
{
    await mailer.SendWelcomeAsync(e.SubscriptionId, ct);
});
```

Handlers are keyed by the **event type name**, so a subscription only receives events of exactly
that type.

## Ordering and failure behaviour

Publication runs in a fixed order, and the order matters:

1. **Persist** — the event is written through `IEventRepository`.
2. **Dispatch** — webhook subscribers are notified through `IWebhookDispatcher`.
3. **Handle** — in-memory handlers run in subscription order.

Failures are treated differently by stage:

- A failure in **persist** or **dispatch** propagates to the caller. The event is not silently lost.
- A failure in an **in-memory handler** is logged and swallowed, and the remaining handlers still
  run. One broken subscriber cannot take down the rest.

Because persistence happens first, an event that reached the store but failed to dispatch is
recoverable — the record is there to retry from.

## Tracing

Each publication opens an activity named `EventAggregator.Publish {eventType}` and the current
`traceparent` is passed to `IEventRepository.PersistAsync`. Store it, and a persisted event can be
tied back to the trace that produced it. Requires `Personix.Tracing`, which comes as a dependency.

## Notes

- Subscriptions are held in an immutable list, so subscribing from inside a running handler — or
  from another thread during publication — is safe.
- The payload is serialised with `System.Text.Json` using the concrete event type.

## Licence

MIT — see [LICENSE](LICENSE).
