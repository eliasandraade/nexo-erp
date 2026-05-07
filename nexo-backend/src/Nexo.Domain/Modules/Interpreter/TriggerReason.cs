namespace Nexo.Domain.Modules.Interpreter;

public enum TriggerReason
{
    NewPromptVersion = 1,
    ModelUpgrade     = 2,
    ManualRequest    = 3,
    RuleChange       = 4,
    DataCorrection   = 5
}
