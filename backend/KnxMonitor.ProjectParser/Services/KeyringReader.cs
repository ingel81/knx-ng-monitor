using System.Xml.Linq;
using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Helpers;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.ProjectParser.Services;

public class KeyringReader : IKeyringReader
{
    private readonly ILogger<KeyringReader> _logger;

    public KeyringReader(ILogger<KeyringReader> logger)
    {
        _logger = logger;
    }

    public async Task<KeyringData> ReadAsync(
        Stream keyringStream,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Keyring password must not be empty", nameof(password));
        }

        var doc = await XDocument.LoadAsync(keyringStream, LoadOptions.None, cancellationToken);
        var root = doc.Root ?? throw new InvalidOperationException("Keyring XML is empty");
        var ns = root.Name.Namespace;

        var created = Attr(root, "Created")
            ?? throw new InvalidOperationException("Keyring missing Created attribute");

        var passwordHash = KeyringCrypto.HashPassword(password);
        var iv = KeyringCrypto.DeriveIv(created);

        var data = new KeyringData
        {
            ProjectName = Attr(root, "Project"),
            CreatedBy = Attr(root, "CreatedBy"),
            Created = created,
            Signature = Attr(root, "Signature"),
        };

        foreach (var backbone in root.Elements(ns + "Backbone"))
        {
            data.BackboneMulticastAddress = Attr(backbone, "MulticastAddress");
            var backboneKey = Attr(backbone, "Key");
            if (!string.IsNullOrEmpty(backboneKey))
            {
                data.BackboneKey = DecryptKey(backboneKey, passwordHash, iv);
            }
        }

        foreach (var devicesElement in root.Elements(ns + "Devices"))
        {
            foreach (var device in devicesElement.Elements(ns + "Device"))
            {
                var kd = new KeyringDevice
                {
                    IndividualAddress = Attr(device, "IndividualAddress") ?? string.Empty,
                };

                var toolKey = Attr(device, "ToolKey");
                if (!string.IsNullOrEmpty(toolKey))
                {
                    kd.ToolKey = DecryptKey(toolKey, passwordHash, iv);
                }

                var mgmtPw = Attr(device, "ManagementPassword");
                if (!string.IsNullOrEmpty(mgmtPw))
                {
                    kd.ManagementPassword = DecryptString(mgmtPw, passwordHash, iv);
                }

                var auth = Attr(device, "Authentication");
                if (!string.IsNullOrEmpty(auth))
                {
                    kd.Authentication = DecryptString(auth, passwordHash, iv);
                }

                data.Devices.Add(kd);
            }
        }

        foreach (var groupAddresses in root.Elements(ns + "GroupAddresses"))
        {
            foreach (var ga in groupAddresses.Elements(ns + "GroupAddress"))
            {
                var kga = new KeyringGroupAddress
                {
                    Address = Attr(ga, "Address") ?? string.Empty,
                };

                var gaKey = Attr(ga, "Key");
                if (!string.IsNullOrEmpty(gaKey))
                {
                    kga.Key = DecryptKey(gaKey, passwordHash, iv);
                }

                data.GroupAddresses.Add(kga);
            }
        }

        _logger.LogInformation(
            "Keyring parsed: {Devices} devices, {GAs} group addresses",
            data.Devices.Count, data.GroupAddresses.Count);

        return data;
    }

    private static string? Attr(XElement element, string name) => element.Attribute(name)?.Value;

    private static byte[] DecryptKey(string encryptedBase64, byte[] key, byte[] iv)
    {
        var ciphertext = Convert.FromBase64String(encryptedBase64);
        return KeyringCrypto.DecryptAes128Cbc(ciphertext, key, iv);
    }

    private static string DecryptString(string encryptedBase64, byte[] key, byte[] iv)
    {
        var ciphertext = Convert.FromBase64String(encryptedBase64);
        var plaintext = KeyringCrypto.DecryptAes128Cbc(ciphertext, key, iv);
        return KeyringCrypto.ExtractPasswordString(plaintext);
    }
}
