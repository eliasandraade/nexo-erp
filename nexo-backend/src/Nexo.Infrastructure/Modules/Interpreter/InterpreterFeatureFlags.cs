using Microsoft.Extensions.Configuration;
using Nexo.Application.Modules.Interpreter.Interfaces;

namespace Nexo.Infrastructure.Modules.Interpreter;

public sealed class InterpreterFeatureFlags : IInterpreterFeatureFlags
{
    public bool EnableClaudeAnalyzer    { get; }
    public bool EnableOpenAIAnalyzer    { get; }
    public bool EnableRuleBasedAnalyzer { get; }
    public bool EnableMemoryRebuild     { get; }

    public InterpreterFeatureFlags(IConfiguration configuration)
    {
        var section = configuration.GetSection("Interpreter:Features");
        EnableClaudeAnalyzer    = section.GetValue("EnableClaudeAnalyzer",    false);
        EnableOpenAIAnalyzer    = section.GetValue("EnableOpenAIAnalyzer",    false);
        EnableRuleBasedAnalyzer = section.GetValue("EnableRuleBasedAnalyzer", true);
        EnableMemoryRebuild     = section.GetValue("EnableMemoryRebuild",     true);
    }
}
