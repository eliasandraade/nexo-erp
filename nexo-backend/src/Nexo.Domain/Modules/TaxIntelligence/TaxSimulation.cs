using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// A what-if comparison of scenarios. It only stores its own inputs and results: running it never
/// creates a calculation, a ledger entry, a credit, a split expectation or an opportunity, and never
/// touches sales or the books. Hypotheses (e.g. a rate not yet fixed by law) are labelled as such
/// in each scenario's input.
/// </summary>
public class TaxSimulation : StoreEntity
{
    private readonly List<TaxSimulationScenario> _scenarios = [];

    private TaxSimulation() { }   // EF Core
    private TaxSimulation(Guid tenantId) : base(tenantId) { }

    public string Name { get; private set; } = string.Empty;
    public string EngineVersion { get; private set; } = string.Empty;
    public Guid CreatedById { get; private set; }

    public IReadOnlyCollection<TaxSimulationScenario> Scenarios => _scenarios.AsReadOnly();

    public static TaxSimulation Create(Guid tenantId, string name, string engineVersion, Guid createdById)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Dê um nome à simulação.");
        return new TaxSimulation(tenantId) { Name = name.Trim(), EngineVersion = engineVersion, CreatedById = createdById };
    }

    public void AddScenario(int index, string name, bool isBaseline, string inputJson, string resultJson, decimal totalTax, decimal netCost)
        => _scenarios.Add(new TaxSimulationScenario(TenantId, Id, index, name, isBaseline, inputJson, resultJson, totalTax, netCost));
}

public class TaxSimulationScenario : StoreEntity
{
    private TaxSimulationScenario() { }   // EF Core

    internal TaxSimulationScenario(Guid tenantId, Guid simulationId, int index, string name, bool isBaseline,
        string inputJson, string resultJson, decimal totalTax, decimal netCost) : base(tenantId)
    {
        SimulationId = simulationId;
        ScenarioIndex = index;
        Name = name.Trim();
        IsBaseline = isBaseline;
        InputJson = inputJson;
        ResultJson = resultJson;
        TotalTax = totalTax;
        NetCost = netCost;
    }

    public Guid SimulationId { get; private set; }
    public int ScenarioIndex { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsBaseline { get; private set; }
    public string InputJson { get; private set; } = "{}";
    public string ResultJson { get; private set; } = "{}";
    public decimal TotalTax { get; private set; }
    /// <summary>What the operation costs after taxes and credits under this scenario.</summary>
    public decimal NetCost { get; private set; }
}
