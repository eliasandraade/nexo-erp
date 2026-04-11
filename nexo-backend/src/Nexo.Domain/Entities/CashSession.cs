using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

/// <summary>
/// Representa um turno de caixa. Apenas uma sessão pode estar Open por tenant por vez.
/// </summary>
public class CashSession : StoreEntity
{
    private CashSession() { }
    private CashSession(Guid tenantId) : base(tenantId) { }

    public CashSessionStatus Status { get; private set; }
    public Guid OpenedByUserId { get; private set; }
    public Guid? ClosedByUserId { get; private set; }
    public decimal OpeningBalance { get; private set; }
    public decimal? ClosingBalance { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public string? Notes { get; private set; }

    // Navigation
    public User? OpenedBy { get; private set; }
    public User? ClosedBy { get; private set; }
    public ICollection<CashMovement> Movements { get; private set; } = [];
    public ICollection<Sale> Sales { get; private set; } = [];

    public static CashSession Open(Guid tenantId, Guid openedByUserId, decimal openingBalance, string? notes = null)
    {
        return new CashSession(tenantId)
        {
            Status           = CashSessionStatus.Open,
            OpenedByUserId   = openedByUserId,
            OpeningBalance   = openingBalance,
            OpenedAt         = DateTime.UtcNow,
            Notes            = notes?.Trim(),
        };
    }

    public void Close(Guid closedByUserId, decimal closingBalance)
    {
        if (Status == CashSessionStatus.Closed)
            throw new InvalidOperationException("Cash session is already closed.");

        Status           = CashSessionStatus.Closed;
        ClosedByUserId   = closedByUserId;
        ClosingBalance   = closingBalance;
        ClosedAt         = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public bool IsOpen => Status == CashSessionStatus.Open;
}
