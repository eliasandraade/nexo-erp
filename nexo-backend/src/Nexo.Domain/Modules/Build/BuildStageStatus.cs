namespace Nexo.Domain.Modules.Build;

public enum BuildStageStatus
{
    Pending    = 1,
    InProgress = 2,
    Completed  = 3,
    Delayed    = 4,
    Blocked    = 5,
}
