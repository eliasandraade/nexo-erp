namespace Nexo.Application.Modules.Interpreter.Interfaces;

/// <summary>
/// Controls which analyzers and features are enabled per environment.
/// Config-backed — no real Claude/OpenAI traffic until explicitly enabled.
/// </summary>
public interface IInterpreterFeatureFlags
{
    bool EnableClaudeAnalyzer   { get; }
    bool EnableOpenAIAnalyzer   { get; }
    bool EnableRuleBasedAnalyzer { get; }
    bool EnableMemoryRebuild    { get; }
}
