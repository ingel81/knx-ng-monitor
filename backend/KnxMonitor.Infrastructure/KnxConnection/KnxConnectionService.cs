using Knx.Falcon;
using Knx.Falcon.Configuration;
using Knx.Falcon.DataSecurity;
using Knx.Falcon.KnxnetIp;
using Knx.Falcon.Sdk;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Services;
using KnxMonitor.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security;

namespace KnxMonitor.Infrastructure.KnxConnection;

public class KnxConnectionService : IKnxConnectionService, IAsyncDisposable, IDisposable
{
    private readonly ILogger<KnxConnectionService> _logger;
    private readonly IProjectCacheService _groupAddressCache;
    private readonly ITelegramQueue _telegramQueue;
    // Scope factory lets this singleton reach scoped repos (active project + keyring blob)
    // at connect time, mirroring KnxAutoConnectWorker. Used ONLY for secure setup.
    private readonly IServiceScopeFactory _scopeFactory;
    private KnxBus? _knxBus;
    private KnxConfiguration? _activeConfiguration;
    private volatile KnxLinkState _state = KnxLinkState.Disconnected;
    // Stored as ticks, not DateTime?: a Nullable<DateTime> is a 16-byte struct, so a status request
    // reading it while the Falcon callback thread writes could observe HasValue paired with a
    // half-written value. A long is read and written atomically via Interlocked; 0 means "none".
    private long _connectedSinceTicks;
    private long _lastDisconnectedAtTicks;
    private volatile bool _manualDisconnect;
    private volatile bool _testInProgress;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    // Serializes the whole connect / cleanup / disconnect / test path. Without it, overlapping
    // callers (auto-connect worker, controller, project activate, import) could race on _knxBus:
    // an old bus never disposed (leak) with GroupMessageReceived still subscribed => duplicate
    // telegram delivery + duplicate persistence. Not reentrant — internal helpers below assume the
    // lock is already held and must NOT re-acquire it.
    private readonly SemaphoreSlim _busLock = new(1, 1);

    public event EventHandler<KnxTelegram>? TelegramReceived;
    public bool IsConnected => _state == KnxLinkState.Connected;
    public KnxLinkState State => _state;
    public DateTime? ConnectedSince => ReadInstant(ref _connectedSinceTicks);
    public DateTime? LastDisconnectedAt => ReadInstant(ref _lastDisconnectedAtTicks);
    public bool ManualDisconnect => _manualDisconnect;
    public bool TestInProgress => _testInProgress;

    public KnxConnectionService(
        ILogger<KnxConnectionService> logger,
        IProjectCacheService groupAddressCache,
        ITelegramQueue telegramQueue,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _groupAddressCache = groupAddressCache;
        _telegramQueue = telegramQueue;
        _scopeFactory = scopeFactory;

        // Surface DPT decode failures (first few only, to avoid log spam on a busy bus).
        DptConverter.OnError = (dpt, ex) =>
        {
            if (Interlocked.Increment(ref _decodeErrorLogged) <= 5)
            {
                _logger.LogWarning(ex, "DPT decode failed for {Dpt} (falling back to hex)", dpt);
            }
        };
    }

    private int _decodeErrorLogged;

    public async Task<bool> ConnectAsync(KnxConfiguration configuration)
    {
        await _busLock.WaitAsync();
        try
        {
            return await ConnectCoreAsync(configuration);
        }
        finally
        {
            _busLock.Release();
        }
    }

    // Lock-free connect body. Callers MUST hold _busLock (public ConnectAsync, or
    // TestConnectionAsync which owns the lock for its whole span).
    private async Task<bool> ConnectCoreAsync(KnxConfiguration configuration)
    {
        try
        {
            // A manual ConnectAsync clears any prior manual-disconnect latch.
            _manualDisconnect = false;
            SetState(KnxLinkState.Connecting);

            // Tear down a previous bus without latching manual-disconnect.
            await CleanupBusConnection();

            _logger.LogInformation("Connecting to KNX bus at {IpAddress}:{Port} via {ConnectionType}",
                configuration.IpAddress, configuration.Port, configuration.ConnectionType);

            // Resolve the active project's keyring blob ONCE (if any). Used both for the optional
            // IP-Secure tunnel credentials and for Data Secure group security below. Defensive:
            // a null blob means everything stays on the exact current non-secure path.
            var keyringBlob = await TryGetActiveKeyringBlobAsync();

            // Create KNX bus instance
            _knxBus = new KnxBus(await BuildConnectorParameters(configuration, keyringBlob));

            // Apply KNX Data Secure group communication BEFORE subscribing to GroupMessageReceived
            // (Falcon requires GroupCommunicationSecurity to be set before any group communication /
            // event subscription). No-op + exact current behavior when no keyring blob is present.
            await TryApplyGroupSecurityAsync(_knxBus, keyringBlob);

            // Subscribe to events before connecting
            _knxBus.GroupMessageReceived += OnGroupMessageReceived;
            _knxBus.ConnectionStateChanged += OnConnectionStateChanged;

            // Connect to the bus
            await _knxBus.ConnectAsync(_cancellationTokenSource.Token);

            _activeConfiguration = configuration;
            SetState(KnxLinkState.Connected);

            _logger.LogInformation("Successfully connected to KNX bus. InterfaceAddress: {IndividualAddress}",
                _knxBus.InterfaceConfiguration.IndividualAddress);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to KNX bus");
            SetState(KnxLinkState.Disconnected);
            await CleanupBusConnection();
            return false;
        }
    }

    /// <summary>
    /// Probes a configuration with a throwaway bus. If a live link is currently up, it is
    /// paused first (its tunnel freed) so gateways that allow only a single tunnel can still
    /// accept the probe, and restored afterwards. The brief recording gap is an accepted
    /// tradeoff — the UI warns the user before triggering this while live. The
    /// <see cref="TestInProgress"/> flag keeps the auto-connect worker out of the window.
    /// </summary>
    /// <summary>
    /// How long a routing probe waits for a first telegram before reporting
    /// <see cref="ConnectionTestOutcome.JoinedWithoutTraffic"/>. Long enough that a normally
    /// busy installation is recognised, short enough to keep the paused-live-link gap small.
    /// </summary>
    private static readonly TimeSpan RoutingTrafficWindow = TimeSpan.FromSeconds(5);

    public async Task<ConnectionTestOutcome> TestConnectionAsync(KnxConfiguration configuration)
    {
        // Hold the bus lock for the entire probe: the pause, the throwaway probe and the restore
        // all touch _knxBus, and the restore calls ConnectCoreAsync (not the public, lock-taking
        // ConnectAsync — the semaphore is not reentrant).
        await _busLock.WaitAsync();
        // Latch the test-in-progress guard FIRST, before reading IsConnected, so the auto-connect
        // worker cannot slip a reconnect into the gap between the check and the pause below.
        _testInProgress = true;
        // Capture and pause the live link (non-latching) so we don't open a second tunnel.
        var liveConfig = IsConnected ? _activeConfiguration : null;
        if (liveConfig != null)
        {
            _logger.LogInformation("Pausing live link for test probe (freeing tunnel)");
            await CleanupBusConnection();
            SetState(KnxLinkState.Disconnected);
        }

        KnxBus? probe = null;
        try
        {
            // Test probe stays a plain reachability check (no secure credentials, no group
            // security). Passing null keyringBlob => identical to the previous behavior
            // (UseSecureTunnel is the only thing that could change params, and with a null blob it
            // falls straight through to the existing non-secure tunnel).
            probe = new KnxBus(await BuildConnectorParameters(configuration, null));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await probe.ConnectAsync(cts.Token);

            if (probe.ConnectionState != BusConnectionState.Connected)
            {
                _logger.LogInformation("Test connection to {IpAddress}:{Port} => failed",
                    configuration.IpAddress, configuration.Port);
                return ConnectionTestOutcome.Failed;
            }

            // Tunneling has a real handshake, so reaching Connected means reachable. Routing does
            // not: the multicast join is local and always succeeds, even when the group can never
            // be reached (router in another VLAN, container on a bridge network, multicast not
            // forwarded). Reporting that as success sends people looking in the wrong place, so
            // wait for an actual telegram before calling it good.
            if (configuration.ConnectionType != Core.Enums.ConnectionType.Routing)
            {
                _logger.LogInformation("Test connection to {IpAddress}:{Port} => success",
                    configuration.IpAddress, configuration.Port);
                return ConnectionTestOutcome.Success;
            }

            var sawTraffic = await WaitForRoutingTrafficAsync(probe);
            _logger.LogInformation(
                "Routing probe on {IpAddress}:{Port} joined the multicast group => {Result}",
                configuration.IpAddress, configuration.Port,
                sawTraffic ? "traffic observed" : $"no traffic within {RoutingTrafficWindow.TotalSeconds}s");
            return sawTraffic ? ConnectionTestOutcome.Success : ConnectionTestOutcome.JoinedWithoutTraffic;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Test connection to {IpAddress}:{Port} failed",
                configuration.IpAddress, configuration.Port);
            return ConnectionTestOutcome.Failed;
        }
        finally
        {
            if (probe != null)
            {
                try { await probe.DisposeAsync(); } catch { /* best effort */ }
            }

            // Restore the live link if we paused it. ConnectCoreAsync is non-latching and assumes
            // the lock is held (it is — we own it for the whole method).
            if (liveConfig != null)
            {
                try
                {
                    await ConnectCoreAsync(liveConfig);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore live link after test probe; "
                        + "auto-connect worker will retry");
                }
            }

            _testInProgress = false;
            _busLock.Release();
        }
    }

    /// <summary>
    /// Listens on a probe bus for the first group telegram, up to <see cref="RoutingTrafficWindow"/>.
    /// Purely passive — nothing is written to the bus.
    /// </summary>
    private static async Task<bool> WaitForRoutingTrafficAsync(KnxBus probe)
    {
        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnProbeMessage(object? sender, GroupEventArgs e) => received.TrySetResult(true);

        probe.GroupMessageReceived += OnProbeMessage;
        try
        {
            var completed = await Task.WhenAny(received.Task, Task.Delay(RoutingTrafficWindow));
            return completed == received.Task;
        }
        finally
        {
            probe.GroupMessageReceived -= OnProbeMessage;
        }
    }

    // internal (not private) so the test project can assert the tunneling/routing mapping
    // without spinning up a real bus. See KnxMonitor.Infrastructure.Tests.
    internal async Task<ConnectorParameters> BuildConnectorParameters(KnxConfiguration configuration, ProjectKeyringBlob? keyringBlob)
    {
        if (configuration.ConnectionType == Core.Enums.ConnectionType.Tunneling)
        {
            // NEW (opt-in, guarded): KNX IP Secure tunnel. Only attempted when the config
            // explicitly enables it (UseSecureTunnel, default false for everyone today). When the
            // branch is not taken — i.e. the default — the existing IpTunnelingConnectorParameters
            // path below is byte-for-byte unchanged.
            if (configuration.UseSecureTunnel)
            {
                var secure = await TryBuildSecureTunnelParameters(configuration, keyringBlob);
                if (secure != null)
                {
                    return secure;
                }
                // Could not derive secure credentials (e.g. no keyring, or the keyring has no
                // matching tunnel slot) → fall through to the plain tunnel rather than fail.
                _logger.LogWarning(
                    "UseSecureTunnel is set but secure tunnel parameters could not be built; "
                    + "falling back to the non-secure tunnel");
            }

            return new IpTunnelingConnectorParameters(
                configuration.IpAddress,
                configuration.Port,
                IpProtocol.Udp,
                true /* NAT mode */);
        }

        // KNXnet/IP routing: join the multicast group instead of opening a tunnel to one
        // interface. Unlike tunneling — where the gateway hands out the source address — a
        // router-mode endpoint sends under its OWN individual address, so the configured
        // PhysicalAddress is passed through here. Falcon would otherwise fall back to its
        // 0.0.1 default, which collides with anything else defaulting the same way.
        // localIPAddress stays null = bind to all interfaces.
        var multicastAddress = IPAddress.Parse(configuration.IpAddress);
        IpRoutingConnectorParameters routingParameters;

        if (IndividualAddress.TryParse(configuration.PhysicalAddress, out var source))
        {
            routingParameters = new IpRoutingConnectorParameters(multicastAddress, source, null);
        }
        else
        {
            // Loud, because the fallback is the one outcome nobody wants: two endpoints both
            // sending as 0.0.1. The UI blocks this, so getting here means the config was set
            // through the API directly.
            _logger.LogWarning(
                "Physical address '{PhysicalAddress}' is not a valid KNX individual address; "
                + "routing falls back to the default source address {DefaultAddress}, which "
                + "collides with any other endpoint doing the same",
                configuration.PhysicalAddress, IpRoutingConnectorParameters.DefaultKnxAddress);
            routingParameters = new IpRoutingConnectorParameters(multicastAddress);
        }

        // Honour a non-standard routing port; 3671 is Falcon's default anyway.
        routingParameters.IpPort = configuration.Port;
        return routingParameters;
    }

    /// <summary>
    /// Builds KNX IP Secure tunnel parameters. UNVERIFIED ON HARDWARE: no secure gateway was
    /// available to test against. This follows the documented Falcon contract:
    /// <see cref="IpUnicastConnectorParameters"/> is abstract, so the concrete secure tunnel is an
    /// <see cref="IpTunnelingConnectorParameters"/> (HostAddress/IpPort/UseNat set), whose secure
    /// credentials (UserId, UserPasswordHash, DeviceAuthenticationCodeHash → IsSecure) are loaded
    /// from the .knxkeys keyring via the supported
    /// <see cref="Knx.Falcon.Configuration.ConnectorParameters.LoadSecurityDataAsync(System.IO.Stream,System.Security.SecureString)"/>.
    /// Returns null when no keyring is present or the keyring yields no matching secure data, so the
    /// caller safely falls back to the plain tunnel.
    /// </summary>
    private async Task<IpTunnelingConnectorParameters?> TryBuildSecureTunnelParameters(
        KnxConfiguration configuration, ProjectKeyringBlob? keyringBlob)
    {
        try
        {
            if (keyringBlob == null || keyringBlob.KeyringFile.Length == 0)
            {
                _logger.LogWarning(
                    "Secure tunnel requested but no keyring blob is available for credential derivation");
                return null;
            }

            // Concrete secure-capable tunnel parameters. IpUnicastConnectorParameters (the type the
            // task contract names) is abstract; IpTunnelingConnectorParameters derives from it and
            // exposes the same IsSecure/HostAddress/IpPort/UseNat/UserId/credential surface.
            var p = new IpTunnelingConnectorParameters(
                configuration.IpAddress,
                configuration.Port,
                IpProtocol.Udp,
                true /* NAT mode */);

            // Load UserId + UserPasswordHash + DeviceAuthenticationCodeHash from the keyring's
            // tunnel slot via the supported Falcon API. Returns true if relevant secure data was
            // found; this also flips IsSecure on. We do NOT hand-derive credentials (that path is
            // exactly what cannot be verified without hardware) — we use the documented loader.
            using var keyringStream = new MemoryStream(keyringBlob.KeyringFile, writable: false);
            using var securePassword = ToSecureString(keyringBlob.KeyringPassword);

            var found = await p.LoadSecurityDataAsync(keyringStream, securePassword);
            if (!found || !p.IsSecure)
            {
                // TODO(secure-hardware): the keyring contained no secure tunnel slot matching this
                // host, or the data was insufficient to make the tunnel secure. Without a real
                // secure interface we cannot confirm the exact keyring layout, so rather than emit
                // a half-configured "secure" tunnel we fall back to the non-secure tunnel.
                _logger.LogWarning(
                    "Keyring did not yield secure tunnel credentials for {Host} (found={Found}, isSecure={IsSecure}); "
                    + "falling back to the non-secure tunnel",
                    configuration.IpAddress, found, p.IsSecure);
                return null;
            }

            _logger.LogInformation(
                "KNX IP Secure tunnel parameters built from keyring (UserId={UserId}) — UNVERIFIED ON HARDWARE",
                p.UserId);
            return p;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build secure tunnel parameters; using the non-secure tunnel");
            return null;
        }
    }

    /// <summary>
    /// Loads the active project's keyring blob (if any). Defensive: any failure (no active
    /// project, no blob, repo error) returns null, which keeps the connection on the exact
    /// current non-secure path. Never throws.
    /// </summary>
    private async Task<ProjectKeyringBlob?> TryGetActiveKeyringBlobAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            var active = await repo.GetActiveProjectAsync();
            if (active == null)
            {
                return null;
            }
            return await repo.GetKeyringBlobAsync(active.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load keyring blob for secure setup; continuing non-secure");
            return null;
        }
    }

    /// <summary>
    /// Wires KNX Data Secure group communication onto the bus when the active project has a stored
    /// keyring blob. Falcon then auto-decrypts incoming secure group telegrams and auto-encrypts
    /// outgoing writes. Fully defensive:
    ///  - No blob  => does nothing, GroupCommunicationSecurity stays null => EXACT current behavior.
    ///  - Any error => logs a warning and continues WITHOUT secure; never breaks a normal connection.
    /// UNVERIFIED ON HARDWARE: no secure gateway was available; this is wired per the documented
    /// Falcon API contract (GroupCommunicationSecurity.Load(stream, SecureString) → settable
    /// KnxBus.GroupCommunicationSecurity) but not tested end-to-end.
    /// </summary>
    private Task TryApplyGroupSecurityAsync(KnxBus bus, ProjectKeyringBlob? keyringBlob)
    {
        try
        {
            if (keyringBlob == null || keyringBlob.KeyringFile.Length == 0)
            {
                // No keyring => stay on the non-secure path. This is the default for every
                // installation that has not uploaded a keyring.
                return Task.CompletedTask;
            }

            using var keyringStream = new MemoryStream(keyringBlob.KeyringFile, writable: false);
            using var securePassword = ToSecureString(keyringBlob.KeyringPassword);

            // Static factory — the supported load path (no public ctor / builder Build()).
            var groupSecurity = GroupCommunicationSecurity.Load(keyringStream, securePassword);

            // Settable property on KnxBus. MUST be assigned before subscribing to
            // GroupMessageReceived (the caller does so right after this).
            bus.GroupCommunicationSecurity = groupSecurity;

            _logger.LogInformation(
                "KNX Data Secure enabled (keyring loaded for active project {ProjectId})",
                keyringBlob.ProjectId);

            // TODO(secure-hardware): with group security set, Falcon delivers already-decrypted
            // values via GroupMessageReceived; secure telegrams it cannot decrypt simply won't
            // raise a decoded event. Designing a visible "encrypted/undecryptable" badge in the UI
            // needs a real secure bus to observe the actual event behavior, so it is intentionally
            // NOT faked here. OnGroupMessageReceived decoding is left unchanged.
        }
        catch (Exception ex)
        {
            // Never let secure setup break a normal connection.
            _logger.LogWarning(ex,
                "Failed to enable KNX Data Secure; continuing WITHOUT secure group communication");
        }

        return Task.CompletedTask;
    }

    private static SecureString ToSecureString(string value)
    {
        var secure = new SecureString();
        foreach (var c in value ?? string.Empty)
        {
            secure.AppendChar(c);
        }
        secure.MakeReadOnly();
        return secure;
    }

    public async Task DisconnectAsync()
    {
        await _busLock.WaitAsync();
        try
        {
            // User-initiated disconnect: latch so the auto-connect worker stays away.
            _manualDisconnect = true;
            await CleanupBusConnection();
            SetState(KnxLinkState.Disconnected);
            _activeConfiguration = null;

            _logger.LogInformation("Disconnected from KNX bus");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from KNX bus");
        }
        finally
        {
            _busLock.Release();
        }
    }

    private async Task CleanupBusConnection()
    {
        if (_knxBus != null)
        {
            try
            {
                _knxBus.GroupMessageReceived -= OnGroupMessageReceived;
                _knxBus.ConnectionStateChanged -= OnConnectionStateChanged;

                await _knxBus.DisposeAsync();
                _knxBus = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up bus connection");
            }
        }
    }

    public Task<KnxConfiguration?> GetActiveConfigurationAsync()
    {
        return Task.FromResult(_activeConfiguration);
    }

    public async Task<bool> WriteGroupValueAsync(string address, string? dptType, string value)
    {
        if (!IsConnected || _knxBus == null)
        {
            _logger.LogWarning("GA write rejected: not connected");
            return false;
        }

        if (!Knx.Falcon.GroupAddress.TryParse(address, out var ga))
        {
            _logger.LogWarning("GA write rejected: invalid address {Address}", address);
            return false;
        }

        var groupValue = DptConverter.Encode(dptType, value);
        if (groupValue == null)
        {
            _logger.LogWarning("GA write rejected: could not encode '{Value}' for DPT {Dpt}", value, dptType);
            return false;
        }

        try
        {
            await _knxBus.WriteGroupValueAsync(ga, groupValue, MessagePriority.Low, _cancellationTokenSource.Token);
            _logger.LogInformation("GA write: {Address} = {Value} (DPT {Dpt})", address, value, dptType);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GA write to {Address} failed", address);
            return false;
        }
    }

    public async Task<bool> ReadGroupValueAsync(string address)
    {
        if (!IsConnected || _knxBus == null)
        {
            _logger.LogWarning("GA read rejected: not connected");
            return false;
        }

        if (!Knx.Falcon.GroupAddress.TryParse(address, out var ga))
        {
            _logger.LogWarning("GA read rejected: invalid address {Address}", address);
            return false;
        }

        try
        {
            // Fire a GroupValueRead; the answering GroupValueResponse arrives via the normal
            // receive path and shows up as a telegram in the live stream / history.
            await _knxBus.RequestGroupValueAsync(ga, MessagePriority.Low, _cancellationTokenSource.Token);
            _logger.LogInformation("GA read request sent to {Address}", address);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GA read request to {Address} failed", address);
            return false;
        }
    }

    private void OnGroupMessageReceived(object? sender, GroupEventArgs e)
    {
        try
        {
            // Map EventType to our MessageType enum (typed enum, not string)
            var messageType = e.EventType switch
            {
                GroupEventType.ValueWrite => MessageType.Write,
                GroupEventType.ValueRead => MessageType.Read,
                GroupEventType.ValueResponse => MessageType.Response,
                _ => MessageType.Read // Default to Read if unknown
            };

            // Get raw value bytes (for storage / hex display)
            var valueBytes = GetValueBytes(e.Value);

            // Convert Falcon telegram to our domain model
            var telegram = new KnxTelegram
            {
                Timestamp = DateTime.UtcNow,
                SourceAddress = e.SourceAddress.ToString(),
                DestinationAddress = e.DestinationAddress.ToString(),
                MessageType = messageType,
                Value = valueBytes,
                ValueDecoded = null, // Will be set after resolving DPT
                Priority = 0, // TODO: Map priority from telegram if available
                Flags = "00"
            };

            // Resolve GroupAddressId and DPT from cache
            string? datapointType = null;
            var groupAddress = _groupAddressCache.GetByAddress(telegram.DestinationAddress);
            if (groupAddress != null)
            {
                telegram.GroupAddressId = groupAddress.Id;
                datapointType = groupAddress.DatapointType;
            }

            // Decode using the original GroupValue (keeps correct bit-size for short DPTs)
            telegram.ValueDecoded = DptConverter.Decode(datapointType, e.Value);

            // Raise event for SignalR broadcasting (now with GroupAddressId set!)
            TelegramReceived?.Invoke(this, telegram);

            // Enqueue for batched persistence. Never blocks the bus thread: a full queue drops the
            // telegram and TelegramPersistenceService counts it, reporting the running total in one
            // aggregated warning instead of one line per lost telegram.
            _telegramQueue.TryEnqueue(telegram);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing KNX telegram");
        }
    }

    private void OnConnectionStateChanged(object? sender, EventArgs e)
    {
        if (sender is KnxBus bus)
        {
            var state = bus.ConnectionState;
            _logger.LogInformation("KNX connection state changed to: {ConnectionState}", state);

            // Map Falcon's bus state onto our link state. A drop here (Closed/Broken/
            // MediumFailure, not user-initiated) leaves State=Disconnected, which the
            // auto-connect worker picks up to reconnect.
            SetState(state == BusConnectionState.Connected
                ? KnxLinkState.Connected
                : KnxLinkState.Disconnected);
        }
    }

    /// <summary>
    /// Single point of truth for link-state transitions, so the connect/disconnect timestamps
    /// cannot drift apart from the state itself.
    /// </summary>
    private void SetState(KnxLinkState next)
    {
        var previous = _state;

        // Timestamps first, state last: a reader that sees Connected must already be able to see
        // the matching ConnectedSince, otherwise the status endpoint can report a connection with
        // no start time.
        if (next == KnxLinkState.Connected)
        {
            if (previous != KnxLinkState.Connected)
            {
                Interlocked.Exchange(ref _connectedSinceTicks, DateTime.UtcNow.Ticks);
            }
        }
        else
        {
            if (previous == KnxLinkState.Connected)
            {
                Interlocked.Exchange(ref _lastDisconnectedAtTicks, DateTime.UtcNow.Ticks);
            }
            Interlocked.Exchange(ref _connectedSinceTicks, 0);
        }

        _state = next;
    }

    private static DateTime? ReadInstant(ref long ticks)
    {
        var value = Interlocked.Read(ref ticks);
        return value == 0 ? null : new DateTime(value, DateTimeKind.Utc);
    }

    private byte[] GetValueBytes(GroupValue? value)
    {
        return value?.Value ?? Array.Empty<byte>();
    }


    public async ValueTask DisposeAsync()
    {
        _cancellationTokenSource.Cancel();
        await DisconnectAsync();
        _cancellationTokenSource.Dispose();
        _busLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        // Prefer DisposeAsync. The host normally calls that on shutdown
        // (IServiceProvider awaits IAsyncDisposable). This sync path is a
        // best-effort fallback and intentionally non-blocking.
        _cancellationTokenSource.Cancel();
        _ = DisposeAsync().AsTask();
        GC.SuppressFinalize(this);
    }
}
