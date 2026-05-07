namespace Nexo.Domain.Modules.Interpreter;

public enum SuggestionSource
{
    LLM         = 1,
    RuleEngine  = 2,
    UserHistory = 3,
    Manual      = 4,
    Hybrid      = 5
}
