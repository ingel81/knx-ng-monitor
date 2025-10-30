using KnxMonitor.Core.Models;

namespace KnxMonitor.Core.Interfaces;

public interface IProjectFeatureDetector
{
    Task<ProjectFeatures> DetectFeaturesAsync(Stream fileStream);
}
