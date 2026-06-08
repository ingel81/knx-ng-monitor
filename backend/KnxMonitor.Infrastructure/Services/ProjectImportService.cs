using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibraryFeatureDetector = KnxMonitor.ProjectParser.Core.Interfaces.IFeatureDetector;

namespace KnxMonitor.Infrastructure.Services;

public class ProjectImportService
{
    private readonly IImportJobManager _jobManager;
    private readonly IProjectFeatureDetector _featureDetector;
    private readonly LibraryFeatureDetector _libraryFeatureDetector;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<ProjectImportService> _logger;

    public ProjectImportService(
        IImportJobManager jobManager,
        IProjectFeatureDetector featureDetector,
        LibraryFeatureDetector libraryFeatureDetector,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<ProjectImportService> logger)
    {
        _jobManager = jobManager;
        _featureDetector = featureDetector;
        _libraryFeatureDetector = libraryFeatureDetector;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task<ImportJobDto> StartImportAsync(string fileName, byte[] fileData)
    {
        var job = _jobManager.CreateJob(fileName);

        // Store file data for later use (if password required)
        _jobManager.StoreJobData(job.Id, fileName, fileData);

        // Start import in background
        _ = Task.Run(async () => await ExecuteImportAsync(job.Id, fileName, fileData));

        return job;
    }

    public ImportJobDto? GetImportStatus(Guid jobId)
    {
        return _jobManager.GetJob(jobId);
    }

    public async Task<bool> ProvideInputAsync(Guid jobId, ProvideInputDto input)
    {
        var job = _jobManager.GetJob(jobId);
        if (job == null || job.Status != ImportStatus.WaitingForInput)
            return false;

        var jobData = _jobManager.GetJobData(jobId);
        if (jobData == null)
            return false;

        // Get or create context
        var context = _jobManager.GetJobContext(jobId);
        if (context == null)
        {
            var features = _jobManager.GetJobFeatures(jobId);
            context = new ImportContext
            {
                JobId = jobId,
                FileName = jobData.Value.FileName,
                FileData = jobData.Value.FileData,
                DetectedFeatures = features
            };
        }

        // Add provided input to context
        if (input.Type == RequirementType.ProjectPassword)
        {
            context.ProjectPassword = input.Password;
            _jobManager.FulfillRequirement(jobId, RequirementType.ProjectPassword);
        }
        else if (input.Type == RequirementType.KeyringFile && input.KeyringFile != null)
        {
            context.KeyringFile = Convert.FromBase64String(input.KeyringFile);
            _jobManager.FulfillRequirement(jobId, RequirementType.KeyringFile);
        }
        else if (input.Type == RequirementType.KeyringPassword)
        {
            context.KeyringPassword = input.Password;
            _jobManager.FulfillRequirement(jobId, RequirementType.KeyringPassword);
        }

        // Store updated context
        _jobManager.StoreJobContext(jobId, context);

        // After password fulfilled, recheck KNX Secure inside the now-decryptable archive
        // and add Keyring requirements if needed (requirements were unknown until decrypt).
        if (input.Type == RequirementType.ProjectPassword
            && !string.IsNullOrEmpty(context.ProjectPassword)
            && !job.Requirements.Any(r => r.Type == RequirementType.KeyringFile))
        {
            try
            {
                using var detectStream = new MemoryStream(jobData.Value.FileData, writable: false);
                var unlocked = await _libraryFeatureDetector.DetectAfterUnlockAsync(
                    detectStream,
                    context.ProjectPassword);

                if (unlocked.HasKnxSecure)
                {
                    _logger.LogInformation("Job {JobId}: KNX Secure detected after unlock, adding keyring requirements", jobId);
                    if (context.DetectedFeatures != null)
                    {
                        context.DetectedFeatures.HasKnxSecureDevices = true;
                        context.DetectedFeatures.RequiresKeyring = true;
                    }
                    _jobManager.StoreJobContext(jobId, context);

                    _jobManager.AddRequirement(jobId, new ImportRequirementDto
                    {
                        Type = RequirementType.KeyringFile,
                        Message = "KNX Secure erkannt. Optional: Keyring-Datei (.knxkeys) hochladen, um Tool-Keys zu speichern.",
                        IsFulfilled = false,
                        IsOptional = true
                    });
                    _jobManager.AddRequirement(jobId, new ImportRequirementDto
                    {
                        Type = RequirementType.KeyringPassword,
                        Message = "Passwort für die Keyring-Datei.",
                        IsFulfilled = false,
                        IsOptional = true
                    });

                    // Job stays in WaitingForInput
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job {JobId}: re-detect after password failed", jobId);
                // fall through; ContinueImport will hit the same error if the password is wrong
            }
        }

        // Check if all requirements are fulfilled
        var refreshed = _jobManager.GetJob(jobId);
        var allFulfilled = refreshed != null && refreshed.Requirements.All(r => r.IsFulfilled);
        if (allFulfilled)
        {
            _logger.LogInformation("Job {JobId}: all requirements fulfilled, resuming import", jobId);
            // Resume import in background
            _ = Task.Run(async () => await ContinueImportAsync(jobId, jobData.Value.FileName, jobData.Value.FileData, context));
        }

        return true;
    }

    public void CancelImport(Guid jobId)
    {
        _jobManager.CancelJob(jobId);
    }

    private async Task ExecuteImportAsync(Guid jobId, string fileName, byte[] fileData)
    {
        try
        {
            _logger.LogInformation("Job {JobId}: ExecuteImportAsync started for {FileName}", jobId, fileName);
            _jobManager.UpdateStep(jobId, ImportStepType.UploadFile, "completed", 100);
            _jobManager.UpdateStep(jobId, ImportStepType.OpenZip, "in-progress", 0);

            // Step 1: Detect features
            using var fileStream = new MemoryStream(fileData);
            var features = await _featureDetector.DetectFeaturesAsync(fileStream);
            _logger.LogInformation("Job {JobId}: features detected (PasswordProtected={Password}, KnxSecure={Secure})",
                jobId, features.IsPasswordProtected, features.HasKnxSecureDevices);

            _jobManager.UpdateStep(jobId, ImportStepType.OpenZip, "completed", 100);
            _jobManager.UpdateStep(jobId, ImportStepType.DetectFeatures, "in-progress", 0);

            // Step 2: Check requirements
            var requirements = new List<ImportRequirementDto>();

            if (features.IsPasswordProtected)
            {
                requirements.Add(new ImportRequirementDto
                {
                    Type = RequirementType.ProjectPassword,
                    Message = "Dieses Projekt ist passwortgeschützt. Bitte geben Sie das Projekt-Passwort ein.",
                    IsFulfilled = false
                });
            }

            if (features.HasKnxSecureDevices && features.RequiresKeyring)
            {
                requirements.Add(new ImportRequirementDto
                {
                    Type = RequirementType.KeyringFile,
                    Message = "KNX Secure Geräte erkannt. Bitte laden Sie die Keyring-Datei (.knxkeys) hoch.",
                    IsFulfilled = false
                });

                requirements.Add(new ImportRequirementDto
                {
                    Type = RequirementType.KeyringPassword,
                    Message = "Bitte geben Sie das Passwort für die Keyring-Datei ein.",
                    IsFulfilled = false
                });
            }

            _jobManager.UpdateStep(jobId, ImportStepType.DetectFeatures, "completed", 100);

            // Store features for later use
            _jobManager.StoreJobFeatures(jobId, features);

            // If requirements exist, wait for user input
            if (requirements.Any())
            {
                _logger.LogInformation("Job {JobId}: waiting for {Count} requirements", jobId, requirements.Count);
                foreach (var req in requirements)
                {
                    _jobManager.AddRequirement(jobId, req);
                }

                _jobManager.UpdateStatus(jobId, ImportStatus.WaitingForInput);
                return; // Wait for ProvideInputAsync to be called
            }

            // No requirements, continue with import
            await ContinueImportAsync(jobId, fileName, fileData, new ImportContext
            {
                JobId = jobId,
                FileName = fileName,
                FileData = fileData,
                DetectedFeatures = features
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: ExecuteImportAsync failed", jobId);
            _jobManager.FailJob(jobId, ex.Message);
        }
    }

    private async Task ContinueImportAsync(Guid jobId, string fileName, byte[] fileData, ImportContext context)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var projectRepository = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var parserService = scope.ServiceProvider.GetRequiredService<IKnxProjectParserService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<IGroupAddressCacheService>();

        try
        {
            _logger.LogInformation("Job {JobId}: ContinueImportAsync started", jobId);
            _jobManager.UpdateStatus(jobId, ImportStatus.Importing);
            _jobManager.UpdateStep(jobId, ImportStepType.CheckPassword, "in-progress", 0);

            context.ProgressCallback = (stepName, progress) =>
            {
                if (Enum.TryParse<ImportStepType>(stepName, out var stepType))
                {
                    var status = progress >= 100 ? "completed" : "in-progress";
                    _jobManager.UpdateStep(jobId, stepType, status, progress);
                }
            };

            var project = new Project
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                ImportDate = DateTime.UtcNow,
                IsActive = false
            };

            await projectRepository.AddAsync(project);

            using var fileStream = new MemoryStream(fileData);
            var (groupAddresses, devices) = await parserService.ParseProjectFileAsync(fileStream, project.Id, context);
            _logger.LogInformation("Job {JobId}: parser produced {Addresses} addresses, {Devices} devices",
                jobId, groupAddresses.Count, devices.Count);

            _jobManager.UpdateStep(jobId, ImportStepType.Save, "in-progress", 0);

            project.GroupAddresses = groupAddresses;
            project.Devices = devices;

            // Auto-activate when no other project is active yet (first import).
            var existingActive = await projectRepository.GetActiveProjectAsync();
            if (existingActive == null)
            {
                project.IsActive = true;
                _logger.LogInformation("Job {JobId}: no active project, auto-activating {ProjectId}", jobId, project.Id);
            }

            await projectRepository.UpdateAsync(project);

            _jobManager.UpdateStep(jobId, ImportStepType.Save, "completed", 100);
            _jobManager.UpdateStep(jobId, ImportStepType.RefreshCache, "in-progress", 0);

            if (project.IsActive)
            {
                await cacheService.RefreshAsync();
            }

            _jobManager.UpdateStep(jobId, ImportStepType.RefreshCache, "completed", 100);

            var etsVersion = context.DetectedFeatures?.EtsVersion ?? EtsVersion.Unknown;
            var hasKnxSecure = context.DetectedFeatures?.HasKnxSecureDevices ?? false;

            _jobManager.CompleteJob(
                jobId,
                project.Id,
                project.Name,
                groupAddresses.Count,
                devices.Count,
                etsVersion,
                hasKnxSecure
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: ContinueImportAsync failed", jobId);
            _jobManager.FailJob(jobId, ex.Message);
        }
    }
}
