using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// A tax credit that came from an acquisition document. The header holds the facts of its origin
/// and never changes; everything that happens to the credit is a <see cref="TaxCreditLedgerEntry"/>
/// (append-only), so the balance on any date and the reason for each movement can be rebuilt.
/// IBS and CBS credits are separate credits — one never pays the other (LC 214/2025, art. 47, § 1º, I).
/// </summary>
public class TaxCredit : StoreEntity
{
    private readonly List<TaxCreditLedgerEntry> _entries = [];

    private TaxCredit() { }   // EF Core
    private TaxCredit(Guid tenantId) : base(tenantId) { }

    public CreditTax Tax { get; private set; }
    /// <summary>Kind of document the credit came from (e.g. "NF-e").</summary>
    public string SourceDocumentType { get; private set; } = string.Empty;
    /// <summary>Access key of the electronic document (44 digits) — required to appropriate (art. 47, § 1º, II).</summary>
    public string? SourceDocumentKey { get; private set; }
    public string? SupplierDocument { get; private set; }
    public DateOnly AcquiredOn { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
    /// <summary>Tax stated on the acquisition document.</summary>
    public decimal OriginalAmount { get; private set; }
    /// <summary>Part of it that may become a credit.</summary>
    public decimal EligibleAmount { get; private set; }
    /// <summary>Rule that made it eligible (key@version), or why it is only a managerial estimate.</summary>
    public string EligibilityBasis { get; private set; } = string.Empty;
    public Guid? SourceCalculationId { get; private set; }

    public IReadOnlyCollection<TaxCreditLedgerEntry> Entries => _entries.AsReadOnly();

    public static TaxCredit Register(
        Guid tenantId, CreditTax tax, string sourceDocumentType, string? sourceDocumentKey, string? supplierDocument,
        DateOnly acquiredOn, DateOnly? expiresOn, decimal originalAmount, decimal eligibleAmount, string eligibilityBasis,
        Guid? sourceCalculationId, Guid actorId, string actorName)
    {
        if (originalAmount <= 0) throw new DomainException("O valor do crédito precisa ser positivo.");
        if (eligibleAmount < 0 || eligibleAmount > originalAmount)
            throw new DomainException("O valor elegível vai de zero ao valor destacado no documento.");
        if (sourceDocumentKey is not null && (sourceDocumentKey.Length != 44 || !sourceDocumentKey.All(char.IsAsciiDigit)))
            throw new DomainException("A chave de acesso do documento tem 44 dígitos.");
        if (string.IsNullOrWhiteSpace(eligibilityBasis)) throw new DomainException("Informe a base da elegibilidade.");

        var c = new TaxCredit(tenantId)
        {
            Tax = tax,
            SourceDocumentType = sourceDocumentType.Trim(),
            SourceDocumentKey = sourceDocumentKey,
            SupplierDocument = string.IsNullOrWhiteSpace(supplierDocument) ? null : supplierDocument.Trim(),
            AcquiredOn = acquiredOn,
            ExpiresOn = expiresOn,
            OriginalAmount = originalAmount,
            EligibleAmount = eligibleAmount,
            EligibilityBasis = eligibilityBasis.Trim(),
            SourceCalculationId = sourceCalculationId,
        };
        c.Append(TaxCreditEntryType.Recognized, eligibleAmount, null, "Crédito identificado no documento de aquisição.", acquiredOn, actorId, actorName);
        return c;
    }

    public TaxCreditBalance Balance() => TaxCreditBalance.Of(EligibleAmount, _entries);

    public TaxCreditLedgerEntry Appropriate(decimal amount, string reason, DateOnly onDate, Guid actorId, string actorName)
    {
        Positive(amount);
        if (SourceDocumentKey is null)
            throw new DomainException("Sem a chave do documento fiscal eletrônico o crédito não pode ser apropriado (LC 214/2025, art. 47, § 1º, II).", "tax_credit_document");
        if (amount > Balance().NotAppropriated)
            throw new DomainException("O valor passa do que ainda pode ser apropriado.", "tax_credit_amount");
        return Append(TaxCreditEntryType.Appropriated, amount, null, reason, onDate, actorId, actorName);
    }

    /// <summary>
    /// Uses part of the available balance to extinguish a debt of the same tax. The caller serialises
    /// uses of one credit (row lock), and the obligation reference is unique per credit, so the same
    /// balance is never used twice.
    /// </summary>
    public TaxCreditLedgerEntry Use(decimal amount, CreditTax obligationTax, string obligationReference, DateOnly onDate, Guid actorId, string actorName)
    {
        Positive(amount);
        if (obligationTax != Tax)
            throw new DomainException("Crédito de IBS não paga CBS, e vice-versa (LC 214/2025, art. 47, § 1º, I).", "tax_credit_cross_tax");
        if (string.IsNullOrWhiteSpace(obligationReference))
            throw new DomainException("Informe a obrigação em que o crédito foi usado.");
        if (ExpiresOn is not null && onDate > ExpiresOn)
            throw new DomainException("O crédito está vencido.", "tax_credit_expired");
        if (_entries.Any(e => e.Type == TaxCreditEntryType.Used && e.ObligationReference == obligationReference.Trim()))
            throw new ConflictException("Este crédito já foi usado nesta obrigação.");
        if (amount > Balance().Available)
            throw new DomainException("O valor passa do saldo disponível.", "tax_credit_amount");
        return Append(TaxCreditEntryType.Used, amount, obligationReference.Trim(), $"Usado na obrigação {obligationReference.Trim()}.", onDate, actorId, actorName);
    }

    public TaxCreditLedgerEntry Block(decimal amount, string reason, DateOnly onDate, Guid actorId, string actorName)
    {
        Positive(amount);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Informe por que o crédito está bloqueado.");
        if (amount > Balance().Available)
            throw new DomainException("Só o saldo disponível pode ser bloqueado.", "tax_credit_amount");
        return Append(TaxCreditEntryType.Blocked, amount, null, reason, onDate, actorId, actorName);
    }

    public TaxCreditLedgerEntry Unblock(decimal amount, string reason, DateOnly onDate, Guid actorId, string actorName)
    {
        Positive(amount);
        if (amount > Balance().Blocked)
            throw new DomainException("O valor passa do que está bloqueado.", "tax_credit_amount");
        return Append(TaxCreditEntryType.Unblocked, amount, null, reason, onDate, actorId, actorName);
    }

    public TaxCreditLedgerEntry ReverseAppropriation(decimal amount, string reason, DateOnly onDate, Guid actorId, string actorName)
    {
        Positive(amount);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Informe o motivo do estorno.");
        if (amount > Balance().Available)
            throw new DomainException("Só o saldo disponível pode ser estornado.", "tax_credit_amount");
        return Append(TaxCreditEntryType.Reversed, amount, null, reason, onDate, actorId, actorName);
    }

    /// <summary>Writes off what is still available once the credit's term has passed.</summary>
    public TaxCreditLedgerEntry Expire(DateOnly onDate, Guid actorId, string actorName)
    {
        if (ExpiresOn is null || onDate <= ExpiresOn)
            throw new DomainException("O crédito ainda não venceu.", "tax_credit_not_expired");
        var available = Balance().Available;
        if (available <= 0) throw new DomainException("Não há saldo disponível para baixar.", "tax_credit_amount");
        return Append(TaxCreditEntryType.Expired, available, null, $"Prazo de uso encerrado em {ExpiresOn:dd/MM/yyyy}.", onDate, actorId, actorName);
    }

    private TaxCreditLedgerEntry Append(TaxCreditEntryType type, decimal amount, string? obligationReference, string reason, DateOnly onDate, Guid actorId, string actorName)
    {
        var entry = new TaxCreditLedgerEntry(TenantId, Id, type, decimal.Round(amount, 2), obligationReference, reason, onDate, actorId, actorName);
        _entries.Add(entry);
        SetUpdatedAt();
        return entry;
    }

    private static void Positive(decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new DomainException("Informe um valor positivo em centavos.");
    }
}

/// <summary>One movement of a credit. Never updated or deleted.</summary>
public class TaxCreditLedgerEntry : StoreEntity
{
    private TaxCreditLedgerEntry() { }   // EF Core

    internal TaxCreditLedgerEntry(Guid tenantId, Guid creditId, TaxCreditEntryType type, decimal amount,
        string? obligationReference, string reason, DateOnly occurredOn, Guid actorId, string actorName) : base(tenantId)
    {
        OccurredOn = occurredOn;
        CreditId = creditId;
        Type = type;
        Amount = amount;
        ObligationReference = obligationReference;
        Reason = reason;
        ActorId = actorId;
        ActorName = actorName;
    }

    public Guid CreditId { get; private set; }
    public TaxCreditEntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string? ObligationReference { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    /// <summary>Business date the movement refers to (the obligation's date for a use) — never after the day it was recorded.</summary>
    public DateOnly OccurredOn { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = string.Empty;
}

/// <summary>Balance of a credit derived from its entries.</summary>
public sealed record TaxCreditBalance(
    decimal Eligible, decimal Appropriated, decimal Used, decimal Blocked, decimal Available, decimal NotAppropriated)
{
    public static TaxCreditBalance Of(decimal eligible, IEnumerable<TaxCreditLedgerEntry> entries)
    {
        var list = entries as IReadOnlyCollection<TaxCreditLedgerEntry> ?? entries.ToList();
        decimal Sum(TaxCreditEntryType t) => list.Where(e => e.Type == t).Sum(e => e.Amount);
        // A reversed appropriation was improper: it leaves the books and does not return to "not appropriated".
        var appropriatedGross = Sum(TaxCreditEntryType.Appropriated);
        var appropriated = appropriatedGross - Sum(TaxCreditEntryType.Reversed);
        var used = Sum(TaxCreditEntryType.Used);
        var blocked = Sum(TaxCreditEntryType.Blocked) - Sum(TaxCreditEntryType.Unblocked);
        var available = appropriated - used - blocked - Sum(TaxCreditEntryType.Expired);
        return new TaxCreditBalance(eligible, appropriated, used, blocked, available, eligible - appropriatedGross);
    }
}
