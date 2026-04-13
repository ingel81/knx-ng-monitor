using System.Text;
using FluentAssertions;
using KnxMonitor.ProjectParser.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnxMonitor.ProjectParser.Tests.Unit.Services;

public class KeyringReaderUnitTests
{
    private static KeyringReader Reader() => new(NullLogger<KeyringReader>.Instance);

    [Fact]
    public async Task ReadAsync_KeyringWithGroupAddresses_ParsesEntries()
    {
        // Synthetic keyring. AES values are not valid ciphertext, but we exercise the parse path
        // for GroupAddresses by providing a non-empty Key attribute. Decryption is lenient (no padding check).
        // Using 16 valid bytes of base64 (exactly AES block) so CBC-decrypt doesn't throw.
        var fakeBase64 = Convert.ToBase64String(new byte[16]);
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Keyring Project="Demo" Created="2024-01-01T00:00:00" Signature="AAAA" xmlns="http://knx.org/xml/keyring/1">
              <GroupAddresses>
                <GroupAddress Address="1/1/1" Key="{fakeBase64}" />
                <GroupAddress Address="1/1/2" />
              </GroupAddresses>
            </Keyring>
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var keyring = await Reader().ReadAsync(stream, "any-password");

        keyring.GroupAddresses.Should().HaveCount(2);
        keyring.GroupAddresses[0].Address.Should().Be("1/1/1");
        keyring.GroupAddresses[0].Key.Should().NotBeNull();
        keyring.GroupAddresses[1].Key.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_MissingCreatedAttribute_Throws()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Keyring Project="X" xmlns="http://knx.org/xml/keyring/1" />
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var act = async () => await Reader().ReadAsync(stream, "x");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Created*");
    }

    [Fact]
    public async Task ReadAsync_EmptyXmlDocument_Throws()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>"));

        var act = async () => await Reader().ReadAsync(stream, "x");

        await act.Should().ThrowAsync<Exception>();
    }
}
