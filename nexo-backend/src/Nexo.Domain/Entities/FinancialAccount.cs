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
    /// <summary>
    /// Saldo inicial (Caixa / Banco): the money the business had at the END of
    /// <see cref="OpeningBalanceDate"/> — everything paid that day is already inside it. The cash flow
    /// starts from it and adds what was realized from the next day on. One cut-off date per company.
    /// </summary>
    public decimal OpeningBalance { get; private set; }
    public DateOnly? OpeningBalanceDate { get; private set; }

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

    public void SetOpeningBalance(decimal amount, DateOnly date)
    {
        if (AccountType is not (FinancialAccountType.Cash or FinancialAccountType.Bank))
            throw new Nexo.Domain.Exceptions.DomainException("Saldo inicial é de contas de caixa ou banco.");
        var cents = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (cents != amount)
            throw new Nexo.Domain.Exceptions.DomainException("Saldo inicial em centavos.");
        if (Math.Abs(cents) > 999_999_999_999.99m)
            throw new Nexo.Domain.Exceptions.DomainException("Saldo inicial fora do limite.");
        OpeningBalance     = cents;          // may be negative (cheque especial)
        OpeningBalanceDate = date;
        SetUpdatedAt();
    }

    public void ClearOpeningBalance()
    {
        OpeningBalance     = 0;
        OpeningBalanceDate = null;
        SetUpdatedAt();
    }

    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
}
