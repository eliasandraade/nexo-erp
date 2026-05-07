namespace Nexo.Domain.Modules.Build;

public enum BuildBudgetStatus
{
    Draft     = 1,
    Sent      = 2,
    Approved  = 3,
    Rejected  = 4,
    Converted = 5, // Converted to a BuildProject
}
