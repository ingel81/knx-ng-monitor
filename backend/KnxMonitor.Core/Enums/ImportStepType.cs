namespace KnxMonitor.Core.Enums;

public enum ImportStepType
{
    UploadFile,
    OpenZip,
    DetectFeatures,
    CheckPassword,
    ParseGroupAddresses,
    ParseDevices,
    ParseSecurity,
    Validate,
    Save,
    RefreshCache
}
