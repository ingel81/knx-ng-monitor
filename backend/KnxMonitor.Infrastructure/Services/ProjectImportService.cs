using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace KnxMonitor.Infrastructure.Services;

public class ProjectImportService
{
    private readonly IImportJobManager _jobManager;
    private readonly IProjectFeatureDetector _featureDetector;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public ProjectImportService(
        IImportJobManager jobManager,
        IProjectFeatureDetector featureDetector,
        IServiceScopeFactory serviceScopeFactory)
    {
        _jobManager = jobManager;
        _featureDetector = featureDetector;
        _serviceScopeFactory = serviceScopeFactory;
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
            Console.WriteLine($"[ImportJob {jobId}] Storing project password");
            context.ProjectPassword = input.Password;
            _jobManager.FulfillRequirement(jobId, RequirementType.ProjectPassword);
        }
        else if (input.Type == RequirementType.KeyringFile && input.KeyringFile != null)
        {
            Console.WriteLine($"[ImportJob {jobId}] Storing keyring file ({input.KeyringFile.Length} chars Base64)");
            // Decode Base64 string to byte array
            context.KeyringFile = Convert.FromBase64String(input.KeyringFile);
            _jobManager.FulfillRequirement(jobId, RequirementType.KeyringFile);
        }
        else if (input.Type == RequirementType.KeyringPassword)
        {
            Console.WriteLine($"[ImportJob {jobId}] Storing keyring password");
            context.KeyringPassword = input.Password;
            _jobManager.FulfillRequirement(jobId, RequirementType.KeyringPassword);
        }

        // Store updated context
        _jobManager.StoreJobContext(jobId, context);

        // Check if all requirements are fulfilled
        var allFulfilled = job.Requirements.All(r => r.IsFulfilled);
        if (allFulfilled)
        {
            Console.WriteLine($"[ImportJob {jobId}] All requirements fulfilled, resuming import");
            Console.WriteLine($"[ImportJob {jobId}] Context: Password={!string.IsNullOrEmpty(context.ProjectPassword)}, KeyringFile={context.KeyringFile != null}, KeyringPassword={!string.IsNullOrEmpty(context.KeyringPassword)}");
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
            Console.WriteLine($"[ImportJob {jobId}] ExecuteImportAsync started");
            _jobManager.UpdateStep(jobId, ImportStepType.UploadFile, "completed", 100);
            _jobManager.UpdateStep(jobId, ImportStepType.OpenZip, "in-progress", 0);

            // Step 1: Detect features
            Console.WriteLine($"[ImportJob {jobId}] Detecting features...");
            using var fileStream = new MemoryStream(fileData);
            var features = await _featureDetector.DetectFeaturesAsync(fileStream);
            Console.WriteLine($"[ImportJob {jobId}] Features detected: Password={features.IsPasswordProtected}, Secure={features.HasKnxSecureDevices}");

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
                Console.WriteLine($"[ImportJob {jobId}] Waiting for {requirements.Count} requirements");
                foreach (var req in requirements)
                {
                    _jobManager.AddRequirement(jobId, req);
                }

                _jobManager.UpdateStatus(jobId, ImportStatus.WaitingForInput);
                return; // Wait for ProvideInputAsync to be called
            }

            // No requirements, continue with import
            Console.WriteLine($"[ImportJob {jobId}] No requirements, continuing with import");
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
            Console.WriteLine($"[ImportJob {jobId}] ERROR in ExecuteImportAsync: {ex.Message}");
            Console.WriteLine($"[ImportJob {jobId}] StackTrace: {ex.StackTrace}");
            _jobManager.FailJob(jobId, ex.Message);
        }
    }

    private async Task ContinueImportAsync(Guid jobId, string fileName, byte[] fileData, ImportContext context)
    {
        // Create a new scope for the background task
        using var scope = _serviceScopeFactory.CreateScope();
        var projectRepository = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var parserService = scope.ServiceProvider.GetRequiredService<IKnxProjectParserService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<IGroupAddressCacheService>();

        try
        {
            Console.WriteLine($"[ImportJob {jobId}] ContinueImportAsync started");
            _jobManager.UpdateStatus(jobId, ImportStatus.Importing);
            _jobManager.UpdateStep(jobId, ImportStepType.CheckPassword, "in-progress", 0);

            // Progress callback
            context.ProgressCallback = (stepName, progress) =>
            {
                Console.WriteLine($"[ImportJob {jobId}] Progress: {stepName} = {progress}%");
                if (Enum.TryParse<ImportStepType>(stepName, out var stepType))
                {
                    var status = progress >= 100 ? "completed" : "in-progress";
                    _jobManager.UpdateStep(jobId, stepType, status, progress);
                }
            };

            // Create project entry
            Console.WriteLine($"[ImportJob {jobId}] Creating project entry in database");
            var project = new Project
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                ImportDate = DateTime.UtcNow,
                IsActive = false
            };

            await projectRepository.AddAsync(project);
            Console.WriteLine($"[ImportJob {jobId}] Project created with ID: {project.Id}");

            // Parse the project
            Console.WriteLine($"[ImportJob {jobId}] Starting parser...");
            using var fileStream = new MemoryStream(fileData);
            var (groupAddresses, devices) = await parserService.ParseProjectFileAsync(fileStream, project.Id, context);
            Console.WriteLine($"[ImportJob {jobId}] Parser completed: {groupAddresses.Count} addresses, {devices.Count} devices");

            _jobManager.UpdateStep(jobId, ImportStepType.Save, "in-progress", 0);

            // Save parsed data
            project.GroupAddresses = groupAddresses;
            project.Devices = devices;
            await projectRepository.UpdateAsync(project);

            _jobManager.UpdateStep(jobId, ImportStepType.Save, "completed", 100);
            _jobManager.UpdateStep(jobId, ImportStepType.RefreshCache, "in-progress", 0);

            // Refresh cache if project is active
            if (project.IsActive)
            {
                await cacheService.RefreshAsync();
            }

            _jobManager.UpdateStep(jobId, ImportStepType.RefreshCache, "completed", 100);

            // Complete job with detected features
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
            Console.WriteLine($"[ImportJob {jobId}] ERROR in ContinueImportAsync: {ex.Message}");
            Console.WriteLine($"[ImportJob {jobId}] StackTrace: {ex.StackTrace}");
            _jobManager.FailJob(jobId, ex.Message);
        }
    }
}
