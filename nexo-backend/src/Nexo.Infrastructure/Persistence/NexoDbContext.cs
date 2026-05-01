using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Common;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Restaurante;
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
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();

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

    // ── Módulo Varejo ─────────────────────────────────────────────────────────
    public DbSet<RetPurchase> RetPurchases => Set<RetPurchase>();
    public DbSet<RetPurchaseItem> RetPurchaseItems => Set<RetPurchaseItem>();
    public DbSet<RetPriceList> RetPriceLists => Set<RetPriceList>();
    public DbSet<RetPriceListItem> RetPriceListItems => Set<RetPriceListItem>();
    public DbSet<RetCustomerPriceList> RetCustomerPriceLists => Set<RetCustomerPriceList>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Apply all IEntityTypeConfiguration implementations in this assembly
        builder.ApplyConfigurationsFromAssembly(typeof(NexoDbContext).Assembly);

        // Default schema
        builder.HasDefaultSchema("nexo");

        // ── Global Query Filters ──────────────────────────────────────────────
        // Applied automatically to every TenantEntity subclass.
        // StoreEntity subclasses get an additional store_id filter.
        ApplyTenantQueryFilters(builder);
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
