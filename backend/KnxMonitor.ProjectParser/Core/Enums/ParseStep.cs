namespace KnxMonitor.ProjectParser.Core.Enums;

public enum ParseStep
{
    OpenZip = 1,
    CheckPassword = 2,
    DetectFeatures = 3,
    ParseGroupAddresses = 4,
    ParseDevices = 5,
    ParseCommunicationObjects = 6,
    ParseTopology = 7,
    ParseLocations = 8,
    ParseFunctions = 9,
    Validate = 10,
    Complete = 11
}
