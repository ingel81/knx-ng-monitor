using System.Collections.Concurrent;
using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Models;

namespace KnxMonitor.Infrastructure.Services;

public class ImportJobManager : IImportJobManager
{
    private readonly ConcurrentDictionary<Guid, ImportJobDto> _jobs = new();
    private readonly ConcurrentDictionary<Guid, (string FileName, byte[] FileData)> _jobData = new();
    private readonly ConcurrentDictionary<Guid, ProjectFeatures> _jobFeatures = new();
    private readonly ConcurrentDictionary<Guid, ImportContext> _jobContexts = new();

    public ImportJobDto CreateJob(string fileName)
    {
        var job = new ImportJobDto
        {
            Id = Guid.NewGuid(),
            Status = ImportStatus.Analyzing,
            CreatedAt = DateTime.UtcNow,
            Steps = CreateInitialSteps()
        };

        _jobs.TryAdd(job.Id, job);
        return job;
    }

    public ImportJobDto? GetJob(Guid jobId)
    {
        return _jobs.TryGetValue(jobId, out var job) ? job : null;
    }

    public void UpdateStatus(Guid jobId, ImportStatus status)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = status;
            if (status == ImportStatus.Completed || status == ImportStatus.Failed || status == ImportStatus.Cancelled)
            {
                job.CompletedAt = DateTime.UtcNow;
            }
        }
    }

    public void UpdateProgress(Guid jobId, int progress)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.OverallProgress = Math.Clamp(progress, 0, 100);
        }
    }

    public void UpdateStep(Guid jobId, ImportStepType stepType, string status, int progress, string? errorMessage = null)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            var step = job.Steps.FirstOrDefault(s => s.Type == stepType);
            if (step != null)
            {
                step.Status = status;
                step.Progress = Math.Clamp(progress, 0, 100);
                step.ErrorMessage = errorMessage;

                if (status == "in-progress" && !step.StartTime.HasValue)
                {
                    step.StartTime = DateTime.UtcNow;
                }
                else if (status == "completed" || status == "failed")
                {
                    step.EndTime = DateTime.UtcNow;
                }
            }
        }
    }

    public void AddRequirement(Guid jobId, ImportRequirementDto requirement)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            // Check if requirement already exists
            var existing = job.Requirements.FirstOrDefault(r => r.Type == requirement.Type);
            if (existing == null)
            {
                job.Requirements.Add(requirement);
            }
        }
    }

    public void FulfillRequirement(Guid jobId, RequirementType type)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            var requirement = job.Requirements.FirstOrDefault(r => r.Type == type);
            if (requirement != null)
            {
                requirement.IsFulfilled = true;
            }
        }
    }

    public void DecrementAttempts(Guid jobId, RequirementType type)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            var requirement = job.Requirements.FirstOrDefault(r => r.Type == type);
            if (requirement != null)
            {
                requirement.RemainingAttempts--;
            }
        }
    }

    public void CompleteJob(Guid jobId, int projectId, string projectName, int groupAddressCount, int deviceCount, EtsVersion etsVersion, bool hasKnxSecure)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = ImportStatus.Completed;
            job.OverallProgress = 100;
            job.CompletedAt = DateTime.UtcNow;
            job.ProjectId = projectId;
            job.ProjectName = projectName;
            job.GroupAddressCount = groupAddressCount;
            job.DeviceCount = deviceCount;
            job.EtsVersion = etsVersion;
            job.HasKnxSecure = hasKnxSecure;
        }
    }

    public void FailJob(Guid jobId, string errorMessage)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = ImportStatus.Failed;
            job.ErrorMessage = errorMessage;
            job.CompletedAt = DateTime.UtcNow;
        }
    }

    public void CancelJob(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = ImportStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
        }
    }

    public void RemoveJob(Guid jobId)
    {
        _jobs.TryRemove(jobId, out _);
        _jobData.TryRemove(jobId, out _);
        _jobFeatures.TryRemove(jobId, out _);
        _jobContexts.TryRemove(jobId, out _);
    }

    public void StoreJobData(Guid jobId, string fileName, byte[] fileData)
    {
        _jobData.TryAdd(jobId, (fileName, fileData));
    }

    public (string FileName, byte[] FileData)? GetJobData(Guid jobId)
    {
        return _jobData.TryGetValue(jobId, out var data) ? data : null;
    }

    public void StoreJobFeatures(Guid jobId, ProjectFeatures features)
    {
        _jobFeatures.TryAdd(jobId, features);
    }

    public ProjectFeatures? GetJobFeatures(Guid jobId)
    {
        return _jobFeatures.TryGetValue(jobId, out var features) ? features : null;
    }

    public void StoreJobContext(Guid jobId, ImportContext context)
    {
        _jobContexts.AddOrUpdate(jobId, context, (_, _) => context);
    }

    public ImportContext? GetJobContext(Guid jobId)
    {
        return _jobContexts.TryGetValue(jobId, out var context) ? context : null;
    }

    private static List<ImportStepDto> CreateInitialSteps()
    {
        return new List<ImportStepDto>
        {
            new() { Type = ImportStepType.UploadFile, Name = "Datei hochladen", Status = "completed", Progress = 100 },
            new() { Type = ImportStepType.OpenZip, Name = "ZIP-Archiv öffnen", Status = "pending", Progress = 0 },
            new() { Type = ImportStepType.DetectFeatures, Name = "Projekt-Features erkennen", Status = "pending", Progress = 0 },
            new() { Type = ImportStepType.CheckPassword, Name = "Passwort prüfen", Status = "pending", Progress = 0 },
            new() { Type = ImportStepType.ParseGroupAddresses, Name = "Gruppenadressen parsen", Status = "pending", Progress = 0 },
            new() { Type = ImportStepType.ParseDevices, Name = "Geräte parsen", Status = "pending", Progress = 0 },
            new() { Type = ImportStepType.ParseSecurity, Name = "Security-Daten verarbeiten", Status = "pending", Progress = 0 },
            new() { Type = ImportStepType.Validate, Name = "Daten validieren", Status = "pending", Progress = 0 },
            new() { Type = ImportStepType.Save, Name = "In Datenbank speichern", Status = "pending", Progress = 0 },
            new() { Type = ImportStepType.RefreshCache, Name = "Cache aktualisieren", Status = "pending", Progress = 0 }
        };
    }
}
