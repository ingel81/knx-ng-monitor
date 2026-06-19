using System.Security;
using FluentAssertions;
using Knx.Falcon.DataSecurity;
using Xunit;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// Deterministic verification of the part of KNX Data Secure (roadmap #4c) that is actually MY
/// code: persisting the raw .knxkeys bytes + password and handing them to Falcon's documented
/// loader. The decryption algorithm itself lives entirely inside the Falcon SDK and only runs on a
/// connected (secure) bus — that end-to-end step needs real hardware and is out of scope here.
/// What we CAN assert without a gateway: that the exact bytes/password the connection service
/// stores and replays are accepted by <see cref="GroupCommunicationSecurity.Load"/> and yield a
/// usable security object (the value assigned to <c>KnxBus.GroupCommunicationSecurity</c>).
///
/// Uses the proprietary secure sample (gitignored under docs/samples/own/); the test no-ops when
/// the fixture is absent (CI / fresh clone), mirroring the parser project's Skip pattern.
/// </summary>
public class SecureKeyringLoadTests
{
    private const string KeyringFile = "TestMitSecure_ets_v5.7.7_secure.knxkeys";
    private const string KeyringPassword = "affe";

    [Fact]
    public void GroupCommunicationSecurity_Load_AcceptsStoredKeyringAndPassword()
    {
        var path = ResolveSample(KeyringFile);
        if (path == null)
        {
            // Proprietary fixture not present (CI / fresh clone) — nothing to verify locally.
            return;
        }

        // Mimic exactly what the connection service does: read the stored raw bytes into a stream
        // and build a SecureString from the stored password, then call Falcon's loader.
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        var secure = ToSecureString(KeyringPassword);

        var act = () => GroupCommunicationSecurity.Load(stream, secure);

        // Falcon must accept our persisted keyring + password and return a usable security object.
        var security = act.Should().NotThrow(
            "the bytes + password the connection service stores must load into Falcon's Data Secure")
            .Subject;
        security.Should().NotBeNull();
    }

    [Fact]
    public void GroupCommunicationSecurity_Load_WrongPassword_Throws()
    {
        var path = ResolveSample(KeyringFile);
        if (path == null)
        {
            return;
        }

        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        var wrong = ToSecureString("not-the-password");

        // A wrong keyring password must fail (the connect path catches this and continues
        // non-secure rather than crashing) — this proves the password is actually exercised.
        var act = () => GroupCommunicationSecurity.Load(stream, wrong);
        act.Should().Throw<Exception>();
    }

    private static SecureString ToSecureString(string value)
    {
        var s = new SecureString();
        foreach (var c in value)
        {
            s.AppendChar(c);
        }
        s.MakeReadOnly();
        return s;
    }

    /// <summary>Walks up from the test output dir to the repo root and resolves docs/samples/own/&lt;name&gt;.</summary>
    private static string? ResolveSample(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "samples", "own", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
