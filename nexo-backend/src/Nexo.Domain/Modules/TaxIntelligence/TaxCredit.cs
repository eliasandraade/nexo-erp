using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// A possible tax credit from an acquisition document. It starts as a <b>potential</b> credit — an
/// estimate that implies no eligibility, appropriation or availability — and only after its
/// requirements are confirmed on record does it become <b>to be appropriated</b>; only then may it be
/// appropriated, and only an appropriated balance can be used. The header holds the facts of its origin;
/// every movement is a <see cref="TaxCreditLedgerEntry"/> (append-only), so the balance on any date and
/// the reason for each movement can be rebuilt. IBS and CBS credits are separate credits — one never
/// pays the other (LC 214/2025, art. 47, § 1º, I).
/// </summary>
public class TaxCredit : StoreEntity
{
    private const string StageCode = "tax_credit_stage";

    /// <summary>Error code of every refusal caused by an amount beyond what the balance allows.</summary>
    public const string AmountErrorCode = "tax_credit_amount";

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
    /// <summary>Estimated part that might become a credit. An estimate — never a validated right.</summary>
    public decimal PotentialAmount { get; private set; }
    /// <summary>Why it was estimated (rule key@version, or that it is only a managerial estimate).</summary>
    public string PotentialBasis { get; private set; } = string.Empty;
    public Guid? SourceCalculationId { get; private set; }
    public TaxCreditStage Stage { get; private set; } = TaxCreditStage.PotentialIdentified;
    /// <summary>Why the requirements were considered met (who confirmed and on what basis).</summary>
    public string? RecognitionBasis { get; private set; }
    public string? DiscardReason { get; private set; }

    public IReadOnlyCollection<TaxCreditLedgerEntry> Entries => _entries.AsReadOnly();

    public static TaxCredit Register(
        Guid tenantId, CreditTax tax, string sourceDocumentType, string? sourceDocumentKey, string? supplierDocument,
        DateOnly acquiredOn, DateOnly? expiresOn, decimal originalAmount, decimal potentialAmount, string potentialBasis,
        Guid? sourceCalculationId)
    {
        if (originalAmount <= 0) throw new DomainException("O valor do crédito precisa ser positivo.");
        if (potentialAmount < 0 || potentialAmount > originalAmount)
            throw new DomainException("O valor potencial vai de zero ao valor destacado no documento.");
        if (sourceDocumentKey is not null && (sourceDocumentKey.Length != 44 || !sourceDocumentKey.All(char.IsAsciiDigit)))
            throw new DomainException("A chave de acesso do documento tem 44 dígitos.");
        if (string.IsNullOrWhiteSpace(potentialBasis)) throw new DomainException("Informe de onde vem a estimativa.");

        var c = new TaxCredit(tenantId)
        {
            Tax = tax,
            SourceDocumentType = sourceDocumentType.Trim(),
            SourceDocumentKey = sourceDocumentKey,
            SupplierDocument = string.IsNullOrWhiteSpace(supplierDocument) ? null : supplierDocument.Trim(),
            AcquiredOn = acquiredOn,
            ExpiresOn = expiresOn,
            OriginalAmount = originalAmount,
            PotentialAmount = potentialAmount,
            PotentialBasis = potentialBasis.Trim(),
            SourceCalculationId = sourceCalculationId,
            Stage = TaxCreditStage.PotentialIdentified,
        };
        // A potential credit writes nothing to the ledger: it is not a right yet.
        return c;
    }

    public TaxCreditBalance Balance() => TaxCreditBalance.Of(PotentialAmount, _entries);

    /// <summary>Where the credit stands overall, from its stage and its ledger.</summary>
    public TaxCreditLifecycle Lifecycle()
    {
        if (Stage != TaxCreditStage.ToBeAppropriated)
            return Stage switch
            {
                TaxCreditStage.RequirementsUnderReview => TaxCreditLifecycle.RequirementsUnderReview,
                TaxCreditStage.Discarded => TaxCreditLifecycle.Discarded,
                _ => TaxCreditLifecycle.PotentialIdentified,
            };
        var b = Balance();
        if (b.Available > 0) return TaxCreditLifecycle.Appropriated;
        if (b.Blocked > 0) return TaxCreditLifecycle.Blocked;
        if (b.ToBeAppropriated > 0) return TaxCreditLifecycle.ToBeAppropriated;
        decimal Sum(TaxCreditEntryType t) => _entries.Where(e => e.Type == t).Sum(e => e.Amount);
        if (Sum(TaxCreditEntryType.Expired) > 0) return TaxCreditLifecycle.Expired;
        if (b.Used > 0) return TaxCreditLifecycle.Used;
        if (Sum(TaxCreditEntryType.Reversed) > 0) return TaxCreditLifecycle.Reversed;
        return TaxCreditLifecycle.ToBeAppropriated;
    }

    /// <summary>Opens the review of the requirements of a potential credit.</summary>
    public void StartRequirementsReview()
    {
        if (Stage != TaxCreditStage.PotentialIdentified)
            throw new DomainException("Só um crédito potencial entra em revisão de requisitos.", StageCode);
        Stage = TaxCreditStage.RequirementsUnderReview;
        SetUpdatedAt();
    }

    /// <summary>
    /// Records that the requirements were confirmed: the credit becomes "to be appropriated" for the
    /// confirmed amount (up to the tax on the document), with the basis on record. Only from here may
    /// it be appropriated.
    /// </summary>
    public TaxCreditLedgerEntry ConfirmToBeAppropriated(decimal amount, string basis, DateOnly onDate, Guid actorId, string actorName)
    {
        if (Stage != TaxCreditStage.RequirementsUnderReview)
            throw new DomainException("Confirme os requisitos depois de abrir a revisão do crédito potencial.", StageCode);
        Positive(amount);
        if (amount > OriginalAmount) throw new DomainException("O valor passa do tributo destacado no documento.", AmountErrorCode);
        if (string.IsNullOrWhiteSpace(basis)) throw new DomainException("Informe a base da confirmação dos requisitos.");
        Stage = TaxCreditStage.ToBeAppropriated;
        RecognitionBasis = basis.Trim();
        // The entry type already says "requirements confirmed": the reason is the basis as given, within its limit.
        return Append(TaxCreditEntryType.Recognized, amount, null, basis.Trim(), onDate, actorId, actorName);
    }

    public void Discard(string reason)
    {
        if (Stage is TaxCreditStage.ToBeAppropriated or TaxCreditStage.Discarded)
            throw new DomainException("Só um crédito potencial ou em revisão pode ser descartado.", StageCode);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Informe por que o crédito foi descartado.");
        Stage = TaxCreditStage.Discarded;
        DiscardReason = reason.Trim();
        SetUpdatedAt();
    }

    public TaxCreditLedgerEntry Appropriate(decimal amount, string reason, DateOnly onDate, Guid actorId, string actorName)
    {
        if (Stage != TaxCreditStage.ToBeAppropriated)
            throw new DomainException("Um crédito potencial não é apropriado: confirme os requisitos antes.", StageCode);
        Positive(amount);
        if (SourceDocumentKey is null)
            throw new DomainException("Sem a chave do documento fiscal eletrônico o crédito não pode ser apropriado (LC 214/2025, art. 47, § 1º, II).", "tax_credit_document");
        if (amount > Balance().ToBeAppropriated)
            throw new DomainException("O valor passa do que ainda pode ser apropriado.", AmountErrorCode);
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
            throw new ConflictException("Este crédito já foi usado nesta obrigação.", "tax_credit_obligation_used");
        if (amount > Balance().Available)
            throw new DomainException("O valor passa do saldo disponível.", AmountErrorCode);
        return Append(TaxCreditEntryType.Used, amount, obligationReference.Trim(), $"Usado na obrigação {obligationReference.Trim()}.", onDate, actorId, actorName);
    }

    public TaxCreditLedgerEntry Block(decimal amount, string reason, DateOnly onDate, Guid actorId, string actorName)
    {
        Positive(amount);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Informe por que o crédito está bloqueado.");
        if (amount > Balance().Available)
            throw new DomainException("Só o saldo disponível pode ser bloqueado.", AmountErrorCode);
        return Append(TaxCreditEntryType.Blocked, amount, null, reason, onDate, actorId, actorName);
    }

    public TaxCreditLedgerEntry Unblock(decimal amount, string reason, DateOnly onDate, Guid actorId, string actorName)
    {
        Positive(amount);
        if (amount > Balance().Blocked)
            throw new DomainException("O valor passa do que está bloqueado.", AmountErrorCode);
        return Append(TaxCreditEntryType.Unblocked, amount, null, reason, onDate, actorId, actorName);
    }

    public TaxCreditLedgerEntry ReverseAppropriation(decimal amount, string reason, DateOnly onDate, Guid actorId, string actorName)
    {
        Positive(amount);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Informe o motivo do estorno.");
        if (amount > Balance().Available)
            throw new DomainException("Só o saldo disponível pode ser estornado.", AmountErrorCode);
        return Append(TaxCreditEntryType.Reversed, amount, null, reason, onDate, actorId, actorName);
    }

    /// <summary>Writes off what is still available once the credit's term has passed.</summary>
    public TaxCreditLedgerEntry Expire(DateOnly onDate, Guid actorId, string actorName)
    {
        if (ExpiresOn is null || onDate <= ExpiresOn)
            throw new DomainException("O crédito ainda não venceu.", "tax_credit_not_expired");
        var available = Balance().Available;
        if (available <= 0) throw new DomainException("Não há saldo disponível para baixar.", AmountErrorCode);
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
    /// <summary>
    /// Client key of the request that wrote the movement, unique per credit: a retried request (double click,
    /// network retry) finds the movement already recorded instead of writing it twice.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    public void KeyedBy(string idempotencyKey)
    {
        if (IdempotencyKey is not null) throw new InvalidOperationException("Idempotency key already set.");
        IdempotencyKey = idempotencyKey;
    }
}

/// <summary>
/// Balance of a credit derived from its entries. <see cref="Potential"/> is the estimate (outside the
/// ledger); <see cref="ToBeAppropriated"/> is what was confirmed and not yet appropriated; only
/// <see cref="Available"/> (appropriated, not used, blocked, reversed or expired) can be used.
/// </summary>
public sealed record TaxCreditBalance(
    decimal Potential, decimal ToBeAppropriated, decimal Appropriated, decimal Used, decimal Blocked, decimal Available)
{
    public static TaxCreditBalance Of(decimal potential, IEnumerable<TaxCreditLedgerEntry> entries)
    {
        var list = entries as IReadOnlyCollection<TaxCreditLedgerEntry> ?? entries.ToList();
        decimal Sum(TaxCreditEntryType t) => list.Where(e => e.Type == t).Sum(e => e.Amount);
        // A reversed appropriation was improper: it leaves the books and does not return to "not appropriated".
        var appropriatedGross = Sum(TaxCreditEntryType.Appropriated);
        var appropriated = appropriatedGross - Sum(TaxCreditEntryType.Reversed);
        var used = Sum(TaxCreditEntryType.Used);
        var blocked = Sum(TaxCreditEntryType.Blocked) - Sum(TaxCreditEntryType.Unblocked);
        var available = appropriated - used - blocked - Sum(TaxCreditEntryType.Expired);
        var recognized = Sum(TaxCreditEntryType.Recognized);
        return new TaxCreditBalance(potential, recognized - appropriatedGross, appropriated, used, blocked, available);
    }
}
