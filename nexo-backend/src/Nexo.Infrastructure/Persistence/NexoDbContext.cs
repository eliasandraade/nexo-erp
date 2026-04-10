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
/// WARNING: Never call IgnoreQueryFilters() in Application or Domain code.
///   Allowed ONLY in: Platform admin services, cross-tenant analytics, and DataSeeder.
/// </summary>
public class NexoDbContext : DbContext
{
    private readonly ICurrentTenant _currentTenant;

    public NexoDbContext(
        DbContextOptions<NexoDbContext> options,
        ICurrentTenant currentTenant)
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    // ── Platform (no tenant_id) ───────────────────────────────────────────────
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<ModuleDefinition> ModuleDefinitions => Set<ModuleDefinition>();
    public DbSet<ModuleSubscription> ModuleSubscriptions => Set<ModuleSubscription>();

    // ── Core (with tenant_id — subject to Global Query Filters) ──────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    // ── CORE business entities ────────────────────────────────────────────────
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
    public DbSet<RestRecipeCard>        RestRecipeCards      => Set<RestRecipeCard>();
    public DbSet<RestRecipeIngredient>  RestRecipeIngredients => Set<RestRecipeIngredient>();

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
        // This is the core of row-level multi-tenant isolation.
        ApplyTenantQueryFilters(builder);
    }

    /// <summary>
    /// Iterates all entity types that inherit TenantEntity and registers a
    /// HasQueryFilter expression that scopes every query to _currentTenant.Id.
    ///
    /// The expression built is equivalent to:
    ///   modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == _currentTenant.Id)
    /// but done generically so it applies to every TenantEntity subclass automatically.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(TenantEntity).IsAssignableFrom(entityType.ClrType)) continue;

            // Build: e => e.TenantId == _currentTenant.Id
            var param = Expression.Parameter(entityType.ClrType, "e");
            var tenantIdProperty = Expression.Property(param, nameof(TenantEntity.TenantId));
            var currentTenantId = Expression.Property(
                Expression.Constant(this, typeof(NexoDbContext)),
                nameof(CurrentTenantIdForFilter));
            var equals = Expression.Equal(tenantIdProperty, currentTenantId);
            var lambda = Expression.Lambda(equals, param);

            builder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    /// <summary>
    /// Exposed as a property so the EF expression tree can reference it.
    /// Returns Guid.Empty when no tenant is resolved (e.g. DataSeeder) — queries
    /// with IgnoreQueryFilters() bypass this entirely.
    /// </summary>
    public Guid CurrentTenantIdForFilter => _currentTenant.IsResolved ? _currentTenant.Id : Guid.Empty;

    /// <summary>
    /// Exposed for TenantSaveChangesInterceptor (same assembly).
    /// Allows the singleton interceptor to access the scoped tenant context
    /// via the DbContext instance it receives in SaveChanges events, avoiding
    /// a captive dependency (singleton → scoped).
    /// </summary>
    internal ICurrentTenant CurrentTenant => _currentTenant;
}
