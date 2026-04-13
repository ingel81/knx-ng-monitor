using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.ProjectParser.Tests.Fixtures;

public class SampleFileFixture : IDisposable
{
    public IProjectParser Parser { get; }
    public IFeatureDetector FeatureDetector { get; }
    public IKeyringReader KeyringReader { get; }

    public SampleFileFixture()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IFeatureDetector, Services.FeatureDetector>();
        services.AddSingleton<IKeyringReader, Services.KeyringReader>();
        services.AddSingleton<IProjectLoader, Loaders.Ets4ProjectLoader>();
        services.AddSingleton<IProjectLoader, Loaders.Ets5ProjectLoader>();
        services.AddSingleton<IProjectLoader, Loaders.Ets6ProjectLoader>();
        services.AddSingleton<IProjectParser, Services.ProjectParser>();

        var provider = services.BuildServiceProvider();

        Parser = provider.GetRequiredService<IProjectParser>();
        FeatureDetector = provider.GetRequiredService<IFeatureDetector>();
        KeyringReader = provider.GetRequiredService<IKeyringReader>();
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}

public class SampleFileInfo
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? Password { get; set; }
    public EtsVersion ExpectedEtsVersion { get; set; }
    public int? ExpectedGroupAddressCount { get; set; }
    public int? ExpectedDeviceCount { get; set; }
}
