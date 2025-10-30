using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Models;

namespace KnxMonitor.Core.Interfaces;

public interface IImportJobManager
{
    ImportJobDto CreateJob(string fileName);
    ImportJobDto? GetJob(Guid jobId);
    void UpdateStatus(Guid jobId, ImportStatus status);
    void UpdateProgress(Guid jobId, int progress);
    void UpdateStep(Guid jobId, ImportStepType stepType, string status, int progress, string? errorMessage = null);
    void AddRequirement(Guid jobId, ImportRequirementDto requirement);
    void FulfillRequirement(Guid jobId, RequirementType type);
    void DecrementAttempts(Guid jobId, RequirementType type);
    void CompleteJob(Guid jobId, int projectId, string projectName, int groupAddressCount, int deviceCount, EtsVersion etsVersion, bool hasKnxSecure);
    void FailJob(Guid jobId, string errorMessage);
    void CancelJob(Guid jobId);
    void RemoveJob(Guid jobId);
    void StoreJobData(Guid jobId, string fileName, byte[] fileData);
    (string FileName, byte[] FileData)? GetJobData(Guid jobId);
    void StoreJobFeatures(Guid jobId, ProjectFeatures features);
    ProjectFeatures? GetJobFeatures(Guid jobId);
    void StoreJobContext(Guid jobId, ImportContext context);
    ImportContext? GetJobContext(Guid jobId);
}
