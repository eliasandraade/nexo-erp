using Nexo.Domain.Common;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// The canonical tax view of an operation: only the facts a tax calculation needs, independent of
/// the module that produced them (sale, purchase, service, manual proposal…). Immutable — new facts
/// about the same source are a new transaction, so a calculation always points at what it read.
/// </summary>
public class TaxTransaction : StoreEntity
{
    private readonly List<TaxTransactionLine> _lines = [];

    private TaxTransaction() { }   // EF Core
    private TaxTransaction(Guid tenantId) : base(tenantId) { }

    public TaxTransactionSource Source { get; private set; }
    /// <summary>Id of the originating record (sale id…); null for manual proposals.</summary>
    public Guid? SourceId { get; private set; }
    public TaxDirection Direction { get; private set; }
    /// <summary>Date of the taxable event (fornecimento, LC 214/2025, art. 10).</summary>
    public DateOnly OperationDate { get; private set; }
    public string? Description { get; private set; }
    /// <summary>Regime of the other party, when known — decides the supplier regime of inbound operations.</summary>
    public TaxRegime? CounterpartyRegime { get; private set; }
    public string? CounterpartyState { get; private set; }
    /// <summary>SHA-256 of the canonical facts; equal facts give an equal hash.</summary>
    public string FactsHash { get; private set; } = string.Empty;
    public Guid CreatedById { get; private set; }

    // Events and dates the law may tell apart. Engine V1 supports only a single, immediate supply
    // (operation date = supply date = document date); the rest is recorded, never silently assumed.
    public TaxSupplyPattern SupplyPattern { get; private set; } = TaxSupplyPattern.SingleSupply;
    public DateOnly? DocumentDate { get; private set; }
    public DateOnly? SupplyDate { get; private set; }
    public DateOnly? PaymentOrAdvanceDate { get; private set; }
    /// <summary>Competence period "yyyy-MM", when the document states one.</summary>
    public string? CompetencePeriod { get; private set; }
    public DateOnly? ExecutionFrom { get; private set; }
    public DateOnly? ExecutionTo { get; private set; }

    public IReadOnlyCollection<TaxTransactionLine> Lines => _lines.AsReadOnly();

    public static TaxTransaction Create(
        Guid tenantId, TaxTransactionSource source, Guid? sourceId, TaxDirection direction, DateOnly operationDate,
        string? description, TaxRegime? counterpartyRegime, string? counterpartyState, string factsHash, Guid createdById)
        => new(tenantId)
        {
            Source = source,
            SourceId = sourceId,
            Direction = direction,
            OperationDate = operationDate,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CounterpartyRegime = counterpartyRegime,
            CounterpartyState = string.IsNullOrWhiteSpace(counterpartyState) ? null : counterpartyState.Trim().ToUpperInvariant(),
            FactsHash = factsHash,
            CreatedById = createdById,
        };

    public void SetTiming(TaxSupplyPattern pattern, DateOnly? documentDate, DateOnly? supplyDate, DateOnly? paymentOrAdvanceDate,
        string? competencePeriod, DateOnly? executionFrom, DateOnly? executionTo)
    {
        SupplyPattern = pattern;
        DocumentDate = documentDate;
        SupplyDate = supplyDate;
        PaymentOrAdvanceDate = paymentOrAdvanceDate;
        CompetencePeriod = string.IsNullOrWhiteSpace(competencePeriod) ? null : competencePeriod.Trim();
        ExecutionFrom = executionFrom;
        ExecutionTo = executionTo;
    }

    public void AddLine(
        int index, TaxItemKind kind, string description, string? ncm, decimal quantity, decimal grossAmount,
        decimal unconditionalDiscount, decimal conditionalDiscount, decimal surcharges, decimal excludedTaxes,
        bool excludedTaxesInformed, string? specialTreatment, string? nbs = null, string? cClassTrib = null)
    {
        if (grossAmount < 0 || unconditionalDiscount < 0 || conditionalDiscount < 0 || surcharges < 0 || excludedTaxes < 0)
            throw new DomainException("Valores da operação não podem ser negativos.");
        if (unconditionalDiscount + conditionalDiscount > grossAmount)
            throw new DomainException("Os descontos passam do valor da linha.");
        _lines.Add(new TaxTransactionLine(TenantId, Id, index, kind, description, ncm, quantity, grossAmount,
            unconditionalDiscount, conditionalDiscount, surcharges, excludedTaxes, excludedTaxesInformed, specialTreatment, nbs, cClassTrib));
    }
}

/// <summary>
/// One item of a tax transaction. <see cref="GrossAmount"/> is the price before any discount;
/// discounts are kept apart by kind because the law treats them differently (LC 214/2025, art. 12).
/// </summary>
public class TaxTransactionLine : StoreEntity
{
    private TaxTransactionLine() { }   // EF Core

    internal TaxTransactionLine(
        Guid tenantId, Guid transactionId, int index, TaxItemKind kind, string description, string? ncm,
        decimal quantity, decimal grossAmount, decimal unconditionalDiscount, decimal conditionalDiscount,
        decimal surcharges, decimal excludedTaxes, bool excludedTaxesInformed, string? specialTreatment, string? nbs, string? cClassTrib)
        : base(tenantId)
    {
        Nbs = string.IsNullOrWhiteSpace(nbs) ? null : nbs.Trim();
        CClassTrib = string.IsNullOrWhiteSpace(cClassTrib) ? null : cClassTrib.Trim();
        TransactionId = transactionId;
        LineIndex = index;
        ItemKind = kind;
        Description = description.Trim();
        Ncm = string.IsNullOrWhiteSpace(ncm) ? null : ncm.Trim();
        Quantity = quantity;
        GrossAmount = grossAmount;
        UnconditionalDiscount = unconditionalDiscount;
        ConditionalDiscount = conditionalDiscount;
        Surcharges = surcharges;
        ExcludedTaxes = excludedTaxes;
        ExcludedTaxesInformed = excludedTaxesInformed;
        SpecialTreatment = string.IsNullOrWhiteSpace(specialTreatment) ? null : specialTreatment.Trim();
    }

    public Guid TransactionId { get; private set; }
    public int LineIndex { get; private set; }
    public TaxItemKind ItemKind { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? Ncm { get; private set; }
    /// <summary>Nomenclatura Brasileira de Serviços — the classification of a service item.</summary>
    public string? Nbs { get; private set; }
    /// <summary>Código de Classificação Tributária do IBS/CBS (cClassTrib), as stated. Not interpreted by V1.</summary>
    public string? CClassTrib { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal GrossAmount { get; private set; }
    /// <summary>Discount stated on the fiscal document that does not depend on a later event (art. 12, § 3º).</summary>
    public decimal UnconditionalDiscount { get; private set; }
    /// <summary>Discount granted under a condition — part of the base (art. 12, § 1º, III).</summary>
    public decimal ConditionalDiscount { get; private set; }
    /// <summary>Interest, fines, freight charged by the supplier and other amounts charged with the price (art. 12, § 1º).</summary>
    public decimal Surcharges { get; private set; }
    /// <summary>ICMS, ISS, PIS/Pasep and Cofins included in the price, out of the base 2026–2032 (art. 12, § 2º, V).</summary>
    public decimal ExcludedTaxes { get; private set; }
    /// <summary>False when the source did not say how much of those taxes is in the price.</summary>
    public bool ExcludedTaxesInformed { get; private set; }
    /// <summary>Regime diferenciado or específico of the item, when the source declares one; null = general regime.</summary>
    public string? SpecialTreatment { get; private set; }
}
