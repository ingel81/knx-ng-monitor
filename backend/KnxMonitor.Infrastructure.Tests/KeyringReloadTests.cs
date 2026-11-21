using FluentAssertions;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Services;
using KnxMonitor.Infrastructure.KnxConnection;
using KnxMonitor.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// The keyring is loaded only when the bus link is built. Uploading a new one, or switching to
/// or away from a project that has one, used to leave the running link on the old keys until
/// the container was restarted (#47).
/// </summary>
public class KeyringReloadTests
{
    private readonly Mock<IProjectRepository> _repo = new();
    private readonly Mock<IKnxProjectParserService> _parser = new();
    private readonly Mock<IKnxConnectionService> _knx = new();

    private ProjectService CreateService() => new(
        _repo.Object,
        Mock.Of<IGroupAddressRepository>(),
        _parser.Object,
        Mock.Of<IProjectCacheService>(),
        _knx.Object);

    private void Active(int? id) =>
        _repo.Setup(r => r.GetActiveProjectAsync()).ReturnsAsync(id is { } i ? new Project { Id = i, IsActive = true } : null);

    private void Project(int id, bool keyring)
    {
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(new Project { Id = id });
        _repo.Setup(r => r.ExistsAsync(id)).ReturnsAsync(true);
        _repo.Setup(r => r.GetKeyringBlobAsync(id)).ReturnsAsync(keyring ? new ProjectKeyringBlob { ProjectId = id } : null);
    }

    [Theory]
    [InlineData(1, true)]   // keyring of the active project: reload
    [InlineData(2, false)]  // keyring of an inactive project: the link does not use it
    public async Task UploadKeyring_ReloadsOnlyForTheActiveProject(int uploadTo, bool expectReload)
    {
        Project(1, keyring: false);
        Project(2, keyring: false);
        Active(1);
        _parser.Setup(p => p.ParseKeyringAsync(It.IsAny<byte[]>(), It.IsAny<string>(), uploadTo))
               .ReturnsAsync(new List<ProjectKeyringKey>());

        await CreateService().UploadKeyringAsync(uploadTo, new byte[] { 1 }, "pw");

        _knx.Verify(k => k.ReloadKeyringAsync(), expectReload ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(false, true, true)]    // to a project with keyring
    [InlineData(true, false, true)]    // away from a project with keyring
    [InlineData(false, false, false)]  // no keyring on either side: link untouched, as before
    public async Task Activate_ReloadsWhenAKeyringIsInvolved(bool oldHasKeyring, bool newHasKeyring, bool expectReload)
    {
        Project(1, oldHasKeyring);
        Project(2, newHasKeyring);
        Active(1);

        (await CreateService().ActivateProjectAsync(2)).Should().BeTrue();

        _knx.Verify(k => k.ReloadKeyringAsync(), expectReload ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task Activate_TheAlreadyActiveProject_DoesNotReload()
    {
        Project(1, keyring: true);
        Active(1);

        await CreateService().ActivateProjectAsync(1);

        _knx.Verify(k => k.ReloadKeyringAsync(), Times.Never());
    }

    [Fact]
    public async Task ReloadKeyring_WhileDisconnected_DoesNothing()
    {
        await using var service = new KnxConnectionService(
            NullLogger<KnxConnectionService>.Instance,
            Mock.Of<IProjectCacheService>(),
            Mock.Of<ITelegramQueue>(),
            Mock.Of<IServiceScopeFactory>());

        (await service.ReloadKeyringAsync()).Should().BeFalse();
        service.State.Should().Be(KnxMonitor.Core.Enums.KnxLinkState.Disconnected);
    }
}

/// <summary>A running link is really rebuilt (and comes back up) when the keyring reloads.</summary>
[Collection(MulticastLoopbackCollection.Name)]
public class KeyringReloadLoopbackTests
{
    [Fact]
    public async Task ReloadKeyring_RebuildsARunningLink()
    {
        await using var service = new KnxConnectionService(
            NullLogger<KnxConnectionService>.Instance,
            Mock.Of<IProjectCacheService>(),
            Mock.Of<ITelegramQueue>(),
            Mock.Of<IServiceScopeFactory>())
        {
            ConfigureParametersForTests = p => ((Knx.Falcon.Configuration.IpRoutingConnectorParameters)p).MulticastTtl = 0
        };

        var connected = await service.ConnectAsync(new KnxConfiguration
        {
            ConnectionType = KnxMonitor.Core.Enums.ConnectionType.Routing,
            IpAddress = "239.255.99.95",
            Port = 3671,
            PhysicalAddress = "1.0.60"
        });
        if (!connected) return; // no multicast in this sandbox, like the other loopback tests

        var before = service.ConnectedSince;
        await Task.Delay(20);

        (await service.ReloadKeyringAsync()).Should().BeTrue();
        service.IsConnected.Should().BeTrue();
        service.ConnectedSince.Should().BeAfter(before!.Value, "the link was built again");
    }
}
