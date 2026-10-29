using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Common;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Domain.Modules.Service;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Domain.Modules.Varejo;

namespace Nexo.Infrastructure.Persistence;

/// <summary>
/// Single application DbContext.
/// All entity configurations are in the Configurations/ folder (IEntityTypeConfiguration).
///
/// Multi-tenant isolation strategy:
///   - Global Query Filters: auto-append WHERE tenant_id = {currentTenant.Id} on every query.
///   - TenantSaveChangesInterceptor: auto-inject TenantId on INSERT, block cross-tenant writes.
///   - IgnoreQueryFilters() is FORBIDDEN on any TenantEntity repository.
///
/// Store isolation strategy (sub-tenant):
///   - StoreEntity subclasses get an ADDITIONAL filter: AND store_id = {currentStore.Id}
///   - TenantSaveChangesInterceptor: auto-inject StoreId on INSERT for StoreEntity subclasses.
///
/// WARNING: Never call IgnoreQueryFilters() in Application or Domain code.
///   Allowed ONLY in: Platform admin services, cross-tenant analytics, and DataSeeder.
/// </summary>
public class NexoDbContext : DbContext
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentStore _currentStore;

    public NexoDbContext(
        DbContextOptions<NexoDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentStore currentStore)
        : base(options)
    {
        _currentTenant = currentTenant;
        _currentStore  = currentStore;
    }

    // ── Platform (no tenant_id) ───────────────────────────────────────────────
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<ModuleDefinition> ModuleDefinitions => Set<ModuleDefinition>();
    public DbSet<ModuleSubscription> ModuleSubscriptions => Set<ModuleSubscription>();
    public DbSet<ModuleSubscriptionEvent> ModuleSubscriptionEvents => Set<ModuleSubscriptionEvent>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<TenantFeatureOverride> TenantFeatureOverrides => Set<TenantFeatureOverride>();
    public DbSet<StripeProcessedEvent> StripeProcessedEvents => Set<StripeProcessedEvent>();

    // ── Store (tenant-scoped but not store-scoped — stores ARE the isolation unit) ──
    public DbSet<Store> Stores => Set<Store>();

    // ── Core (with tenant_id — subject to Global Query Filters) ──────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<TenantNote> TenantNotes => Set<TenantNote>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    // ── CORE business entities (store-scoped via StoreEntity) ────────────────
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<FiscalProfile> FiscalProfiles => Set<FiscalProfile>();
    public DbSet<ErrorEvent> ErrorEvents => Set<ErrorEvent>();
    public DbSet<ProductPurchasePrice> ProductPurchasePrices => Set<ProductPurchasePrice>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<InventoryCount> InventoryCounts => Set<InventoryCount>();
    public DbSet<InventoryCountItem> InventoryCountItems => Set<InventoryCountItem>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();
    public DbSet<FinancialRecurrence> FinancialRecurrences => Set<FinancialRecurrence>();

    // ── Módulo Restaurante ────────────────────────────────────────────────────
    public DbSet<RestArea>              RestAreas            => Set<RestArea>();
    public DbSet<RestTable>             RestTables           => Set<RestTable>();
    public DbSet<RestOrder>             RestOrders           => Set<RestOrder>();
    public DbSet<RestOrderItem>         RestOrderItems       => Set<RestOrderItem>();
    public DbSet<RestOrderItemModifier> RestOrderItemModifiers => Set<RestOrderItemModifier>();
    public DbSet<RestRecipeCard>        RestRecipeCards      => Set<RestRecipeCard>();
    public DbSet<RestRecipeIngredient>  RestRecipeIngredients => Set<RestRecipeIngredient>();
    public DbSet<ProductModifierGroup>  ProductModifierGroups => Set<ProductModifierGroup>();
    public DbSet<ProductModifier>       ProductModifiers      => Set<ProductModifier>();
    public DbSet<FoodServiceSettings>           FoodServiceSettings          => Set<FoodServiceSettings>();
    public DbSet<RestDeliveryOrder>             RestDeliveryOrders           => Set<RestDeliveryOrder>();
    public DbSet<RestDeliveryOrderItem>         RestDeliveryOrderItems       => Set<RestDeliveryOrderItem>();
    public DbSet<RestDeliveryOrderItemModifier> RestDeliveryOrderItemModifiers => Set<RestDeliveryOrderItemModifier>();
    public DbSet<DeliveryZone>                  DeliveryZones                => Set<DeliveryZone>();
    public DbSet<Coupon>                        Coupons                      => Set<Coupon>();
    public DbSet<CouponUsage>                   CouponUsages                 => Set<CouponUsage>();
    public DbSet<RestEmployee>                  RestEmployees                => Set<RestEmployee>();
    public DbSet<RestExpense>                   RestExpenses                 => Set<RestExpense>();

    // ── Módulo Varejo ─────────────────────────────────────────────────────────
    public DbSet<RetPurchase> RetPurchases => Set<RetPurchase>();
    public DbSet<RetPurchaseItem> RetPurchaseItems => Set<RetPurchaseItem>();
    public DbSet<RetPriceList> RetPriceLists => Set<RetPriceList>();
    public DbSet<RetPriceListItem> RetPriceListItems => Set<RetPriceListItem>();
    public DbSet<RetCustomerPriceList> RetCustomerPriceLists => Set<RetCustomerPriceList>();

    // ── Módulo Build (Orken Build — Gestão de Obras) ─────────────────────────
    public DbSet<BuildProject>        BldProjects      => Set<BuildProject>();
    public DbSet<BuildStage>          BldStages        => Set<BuildStage>();
    public DbSet<BuildBudget>         BldBudgets       => Set<BuildBudget>();
    public DbSet<BuildBudgetItem>     BldBudgetItems   => Set<BuildBudgetItem>();
    public DbSet<BuildDailyLog>       BldDailyLogs     => Set<BuildDailyLog>();
    public DbSet<BuildDailyLogPhoto>  BldDailyLogPhotos => Set<BuildDailyLogPhoto>();

    // ── Service module (Orken Service — motor de serviços) ───────────────────
    public DbSet<SvcProfessional>     SvcProfessionals => Set<SvcProfessional>();
    public DbSet<SvcCatalogItem>      SvcCatalogItems  => Set<SvcCatalogItem>();
    public DbSet<SvcSubject>          SvcSubjects      => Set<SvcSubject>();
    public DbSet<SvcRecordEntry>      SvcRecordEntries => Set<SvcRecordEntry>();
    public DbSet<SvcAppointment>      SvcAppointments  => Set<SvcAppointment>();
    public DbSet<SvcTimeBlock>        SvcTimeBlocks    => Set<SvcTimeBlock>();
    public DbSet<SvcOrder>            SvcOrders        => Set<SvcOrder>();
    public DbSet<SvcOrderItem>        SvcOrderItems    => Set<SvcOrderItem>();
    public DbSet<SvcPackage>             SvcPackages             => Set<SvcPackage>();
    public DbSet<SvcPackageItem>         SvcPackageItems         => Set<SvcPackageItem>();
    public DbSet<SvcCustomerPackage>     SvcCustomerPackages     => Set<SvcCustomerPackage>();
    public DbSet<SvcCustomerPackageItem> SvcCustomerPackageItems => Set<SvcCustomerPackageItem>();
    public DbSet<SvcPackageUsage>        SvcPackageUsages        => Set<SvcPackageUsage>();
    public DbSet<SvcPayment>             SvcPayments             => Set<SvcPayment>();
    public DbSet<SvcCommissionEntry>     SvcCommissionEntries    => Set<SvcCommissionEntry>();
    public DbSet<SvcCommissionPayout>    SvcCommissionPayouts    => Set<SvcCommissionPayout>();
    public DbSet<SvcSettings>            SvcSettings             => Set<SvcSettings>();

    // ── Tax Intelligence ─────────────────────────────────────────────────────
    // Catalogue (platform-global — no tenant query filter): legal sources, rules, rule versions.
    public DbSet<TaxLegalSource>          TaxLegalSources          => Set<TaxLegalSource>();
    public DbSet<TaxRule>                 TaxRules                 => Set<TaxRule>();
    public DbSet<TaxRuleVersion>          TaxRuleVersions          => Set<TaxRuleVersion>();
    // Controlled pilot scope (platform-wide): who takes part, kept by the platform.
    public DbSet<TaxPilotScope>           TaxPilotScopes           => Set<TaxPilotScope>();
    public DbSet<TaxPilotParticipant>     TaxPilotParticipants     => Set<TaxPilotParticipant>();
    // Establishment data (tenant + store filter).
    public DbSet<TaxProfile>              TaxProfiles              => Set<TaxProfile>();
    public DbSet<TaxProfileVersion>       TaxProfileVersions       => Set<TaxProfileVersion>();
    public DbSet<TaxTransaction>          TaxTransactions          => Set<TaxTransaction>();
    public DbSet<TaxCalculation>          TaxCalculations          => Set<TaxCalculation>();
    public DbSet<TaxComponent>            TaxComponents            => Set<TaxComponent>();
    public DbSet<TaxRuleExecution>        TaxRuleExecutions        => Set<TaxRuleExecution>();
    public DbSet<TaxSimulation>           TaxSimulations           => Set<TaxSimulation>();
    public DbSet<TaxOpportunity>          TaxOpportunities         => Set<TaxOpportunity>();
    public DbSet<TaxOpportunityRequirement> TaxOpportunityRequirements => Set<TaxOpportunityRequirement>();
    public DbSet<TaxOpportunityEvidence>  TaxOpportunityEvidence   => Set<TaxOpportunityEvidence>();
    public DbSet<TaxDecision>             TaxDecisions             => Set<TaxDecision>();
    public DbSet<TaxExecutionVerification> TaxExecutionVerifications => Set<TaxExecutionVerification>();
    public DbSet<TaxComplianceAssessment> TaxComplianceAssessments => Set<TaxComplianceAssessment>();
    public DbSet<TaxTransactionLine>      TaxTransactionLines      => Set<TaxTransactionLine>();
    public DbSet<TaxCredit>               TaxCredits               => Set<TaxCredit>();
    public DbSet<TaxCreditLedgerEntry>    TaxCreditLedgerEntries   => Set<TaxCreditLedgerEntry>();
    public DbSet<SplitExpectation>        TaxSplitExpectations     => Set<SplitExpectation>();
    public DbSet<SplitSettlement>         TaxSplitSettlements      => Set<SplitSettlement>();
    public DbSet<TaxModuleActivation>     TaxModuleActivations     => Set<TaxModuleActivation>();
    public DbSet<TaxProfessionalReviewRecord> TaxProfessionalReviewRecords => Set<TaxProfessionalReviewRecord>();
    public DbSet<TaxMaterialityPolicy>    TaxMaterialityPolicies   => Set<TaxMaterialityPolicy>();
    public DbSet<TaxRetentionPolicy>      TaxRetentionPolicies     => Set<TaxRetentionPolicy>();
    public DbSet<TaxEvidenceFile>         TaxEvidenceFiles         => Set<TaxEvidenceFile>();
    public DbSet<TaxPlatformDocument>     TaxPlatformDocuments     => Set<TaxPlatformDocument>();
    public DbSet<TaxClassificationCode>   TaxClassificationCodes   => Set<TaxClassificationCode>();
    public DbSet<TaxItemClassification>   TaxItemClassifications   => Set<TaxItemClassification>();
    public DbSet<TaxObligationDefinition> TaxObligationDefinitions => Set<TaxObligationDefinition>();
    public DbSet<TaxComplianceObligationResult> TaxComplianceObligationResults => Set<TaxComplianceObligationResult>();

    // ── Operational Interpretation Engine ────────────────────────────────────
    public DbSet<FinancialMovement>        IntMovements         => Set<FinancialMovement>();
    public DbSet<MovementAttachment>       IntAttachments       => Set<MovementAttachment>();
    public DbSet<ExtractionResult>         IntExtractionResults => Set<ExtractionResult>();
    public DbSet<InterpretationSuggestion> IntSuggestions       => Set<InterpretationSuggestion>();
    public DbSet<UserCorrection>           IntUserCorrections   => Set<UserCorrection>();
    public DbSet<ReprocessLog>             IntReprocessLogs     => Set<ReprocessLog>();
    public DbSet<MovementMemoryProfile>    IntMemoryProfiles    => Set<MovementMemoryProfile>();
    public DbSet<TenantStopword>           IntStopwords         => Set<TenantStopword>();
    public DbSet<MovementAuditLog>         IntAuditLogs         => Set<MovementAuditLog>();

    // ── AI Operations (platform-global — no tenant query filter) ─────────────
    public DbSet<AiProvider>              AiProviders          => Set<AiProvider>();
    public DbSet<InterpreterTelemetry>    InterpreterTelemetry => Set<InterpreterTelemetry>();
    public DbSet<TenantAiLimit>           TenantAiLimits       => Set<TenantAiLimit>();
    public DbSet<StoredPromptVersion>     StoredPromptVersions => Set<StoredPromptVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration implementations in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NexoDbContext).Assembly);

        // Default schema
        modelBuilder.HasDefaultSchema("nexo");

        // ── Global Query Filters ──────────────────────────────────────────────
        // Applied automatically to every TenantEntity subclass.
        // StoreEntity subclasses get an additional store_id filter.
        ApplyTenantQueryFilters(modelBuilder);
    }

    /// <summary>
    /// Iterates all entity types that inherit TenantEntity and registers query filters:
    ///   - TenantEntity (not StoreEntity): WHERE tenant_id = currentTenant.Id
    ///   - StoreEntity: WHERE tenant_id = currentTenant.Id AND store_id = currentStore.Id
    ///
    /// EF Core only allows ONE HasQueryFilter per entity type, so both conditions
    /// are combined into a single lambda.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(StoreEntity).IsAssignableFrom(clrType))
            {
                // Build: e => e.TenantId == CurrentTenantIdForFilter
                //          && e.StoreId  == CurrentStoreIdForFilter
                var param          = Expression.Parameter(clrType, "e");
                var tenantIdProp   = Expression.Property(param, nameof(TenantEntity.TenantId));
                var storeIdProp    = Expression.Property(param, nameof(StoreEntity.StoreId));
                var dbCtxConst     = Expression.Constant(this, typeof(NexoDbContext));
                var tenantIdFilter = Expression.Property(dbCtxConst, nameof(CurrentTenantIdForFilter));
                var storeIdFilter  = Expression.Property(dbCtxConst, nameof(CurrentStoreIdForFilter));
                var tenantEq       = Expression.Equal(tenantIdProp, tenantIdFilter);
                var storeEq        = Expression.Equal(storeIdProp, storeIdFilter);
                var combined       = Expression.AndAlso(tenantEq, storeEq);
                var lambda         = Expression.Lambda(combined, param);

                builder.Entity(clrType).HasQueryFilter(lambda);
            }
            else if (typeof(TenantEntity).IsAssignableFrom(clrType))
            {
                // Build: e => e.TenantId == CurrentTenantIdForFilter
                var param        = Expression.Parameter(clrType, "e");
                var tenantIdProp = Expression.Property(param, nameof(TenantEntity.TenantId));
                var dbCtxConst   = Expression.Constant(this, typeof(NexoDbContext));
                var tenantFilter = Expression.Property(dbCtxConst, nameof(CurrentTenantIdForFilter));
                var equals       = Expression.Equal(tenantIdProp, tenantFilter);
                var lambda       = Expression.Lambda(equals, param);

                builder.Entity(clrType).HasQueryFilter(lambda);
            }
        }
    }

    /// <summary>
    /// Exposed as a property so the EF expression tree can reference it.
    /// Returns Guid.Empty when no tenant is resolved (e.g. DataSeeder) — queries
    /// with IgnoreQueryFilters() bypass this entirely.
    /// </summary>
    public Guid CurrentTenantIdForFilter => _currentTenant.IsResolved ? _currentTenant.Id : Guid.Empty;

    /// <summary>
    /// Exposed as a property so the EF expression tree can reference it.
    /// Returns Guid.Empty when no store is resolved (e.g. DataSeeder, anon requests).
    /// </summary>
    public Guid CurrentStoreIdForFilter => _currentStore.IsResolved ? _currentStore.Id : Guid.Empty;

    /// <summary>
    /// Exposed for TenantSaveChangesInterceptor (same assembly).
    /// Allows the singleton interceptor to access the scoped tenant context
    /// via the DbContext instance it receives in SaveChanges events, avoiding
    /// a captive dependency (singleton → scoped).
    /// </summary>
    internal ICurrentTenant CurrentTenant => _currentTenant;

    /// <summary>
    /// Exposed for TenantSaveChangesInterceptor (same assembly) to auto-inject StoreId.
    /// </summary>
    internal ICurrentStore CurrentStore => _currentStore;
}
