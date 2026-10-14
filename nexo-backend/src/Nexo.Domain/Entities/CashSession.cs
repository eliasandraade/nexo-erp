using Nexo.Domain.Common;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Entities;

/// <summary>
/// Um turno de caixa (uma gaveta física). Um usuário tem no máximo uma sessão aberta por loja.
///
/// O saldo ESPERADO é do domínio, não da tela: abertura + entradas (vendas em dinheiro, suprimentos)
/// − saídas (sangrias, estornos de venda). No fechamento a sessão guarda o esperado, o CONTADO
/// (<see cref="ClosingBalance"/>) e a <see cref="Difference"/> (contado − esperado: positivo = sobra,
/// negativo = falta), congelados para o histórico mesmo que algo mude depois.
/// </summary>
public class CashSession : StoreEntity
{
    private CashSession() { }
    private CashSession(Guid tenantId) : base(tenantId) { }

    public CashSessionStatus Status { get; private set; }
    public Guid OpenedByUserId { get; private set; }
    public Guid? ClosedByUserId { get; private set; }
    public decimal OpeningBalance { get; private set; }
    public decimal? ClosingBalance { get; private set; }   // contado na gaveta ao fechar
    public decimal? ExpectedBalance { get; private set; }  // esperado no momento do fechamento
    public decimal? Difference { get; private set; }       // contado − esperado
    public string? ClosingNotes { get; private set; }
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
        if (openingBalance < 0 || decimal.Round(openingBalance, 2) != openingBalance)
            throw new DomainException("O saldo de abertura deve ser um valor positivo com até duas casas decimais.");

        return new CashSession(tenantId)
        {
            Status           = CashSessionStatus.Open,
            OpenedByUserId   = openedByUserId,
            OpeningBalance   = openingBalance,
            OpenedAt         = DateTime.UtcNow,
            Notes            = notes?.Trim(),
        };
    }

    /// <summary>Expected drawer balance: opening + inflows − outflows.</summary>
    public static decimal ExpectedBalanceFrom(decimal openingBalance, IEnumerable<CashMovement> movements)
        => openingBalance
           + movements.Where(m => m.MovementType.IsInflow()).Sum(m => m.Amount)
           - movements.Where(m => m.MovementType.IsOutflow()).Sum(m => m.Amount);

    /// <summary>
    /// Closes the session against what was counted in the drawer. The expected balance and the
    /// difference are computed here from the session's movements and frozen on the session.
    /// </summary>
    public void Close(Guid closedByUserId, decimal countedBalance, IEnumerable<CashMovement> movements, string? notes = null)
    {
        EnsureOpen();
        if (countedBalance < 0 || decimal.Round(countedBalance, 2) != countedBalance)
            throw new DomainException("O valor contado deve ser positivo e ter até duas casas decimais.");

        var expected = ExpectedBalanceFrom(OpeningBalance, movements.Where(m => m.CashSessionId == Id));

        Status           = CashSessionStatus.Closed;
        ClosedByUserId   = closedByUserId;
        ClosingBalance   = countedBalance;
        ExpectedBalance  = expected;
        Difference       = countedBalance - expected;
        ClosingNotes     = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ClosedAt         = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void EnsureOpen()
    {
        if (Status != CashSessionStatus.Open)
            throw new DomainException("Este caixa já está fechado.");
    }

    public bool IsOpen => Status == CashSessionStatus.Open;
}
