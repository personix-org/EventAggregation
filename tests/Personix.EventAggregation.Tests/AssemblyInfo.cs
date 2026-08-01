using Xunit;

// ActivityHelper (Personix.Tracing) keeps a static ActivitySource. EventAggregatorTracingTests
// calls ActivityHelper.Configure to pin a known source name per test, so tests in this assembly
// must not run concurrently — otherwise one test's Configure call could repoint the source out
// from under another test's listener. Same reasoning, same fix as Personix.Tracing.Tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
