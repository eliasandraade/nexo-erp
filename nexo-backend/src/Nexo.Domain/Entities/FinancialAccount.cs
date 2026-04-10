using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

/// <summary>Nó do plano de contas simplificado por tenant.</summary>
public class FinancialAccount : TenantEntity
{
    private FinancialAccount() { }
    private FinancialAccount(Guid tenantId) : base(tenantId) { }

    public string Code { get; private set; } = string.Empty;       // ex: "1.1.1"
    public string Name { get; private set; } = string.Empty;
    public FinancialAccountType AccountType { get; private set; }
    public Guid? ParentAccountId { get; private set; }
    public bool IsActive { get; private set; }

    // Navigation
    public FinancialAccount? Parent { get; private set; }
    public ICollection<FinancialAccount> Children { get; private set; } = [];
    public ICollection<FinancialTransaction> Transactions { get; private set; } = [];

    public static FinancialAccount Create(
        Guid tenantId,
        string code,
        string name,
        FinancialAccountType accountType,
        Guid? parentAccountId = null)
    {
        return new FinancialAccount(tenantId)
        {
            Code            = code.Trim(),
            Name            = name.Trim(),
            AccountType     = accountType,
            ParentAccountId = parentAccountId,
            IsActive        = true,
        };
    }

    public void Update(string code, string name, Guid? parentAccountId)
    {
        Code            = code.Trim();
        Name            = name.Trim();
        ParentAccountId = parentAccountId;
        SetUpdatedAt();
    }

    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
}
