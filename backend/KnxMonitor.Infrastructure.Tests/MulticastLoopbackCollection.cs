using Xunit;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// The tests that open real multicast buses on this host run one after another: in parallel
/// they can see each other's telegrams and fail depending on timing.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MulticastLoopbackCollection
{
    public const string Name = "Multicast loopback";
}
