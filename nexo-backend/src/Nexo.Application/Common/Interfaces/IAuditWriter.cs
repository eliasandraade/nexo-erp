namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Stages an audit record into the current EF Core DbContext change tracker.
/// The record is committed atomically with the surrounding business transaction.
/// Implementations MUST NOT call SaveChangesAsync — that is the caller's responsibility.
/// </summary>
public interface IAuditWriter
{
    void Stage(
        string actionType,
        string severity,
        string entityType,
        string entityId,
        string description,
        Guid? tenantId = null,
        Guid? actorId = null,
        string? actorName = null,
        string actorType = "user",
        object? metadata = null);
}

/// <summary>Well-known audit action type constants.</summary>
public static class AuditActions
{
    // Auth
    public const string UserLoggedIn           = "user_logged_in";
    public const string UserLoggedOut          = "user_logged_out";
    public const string UserPasswordChanged    = "user_password_changed";
    public const string UserSessionRevoked     = "user_session_revoked";

    // Users
    public const string UserCreated            = "user_created";
    public const string UserUpdated            = "user_updated";
    public const string UserBlocked            = "user_blocked";

    // Operations
    public const string StockAdjustment        = "stock_adjustment";
    public const string StockTransfer          = "stock_transfer";
    public const string CashOpen               = "cash_open";
    public const string CashMovement           = "cash_movement";
    public const string CashClose              = "cash_close";
    public const string SaleCompleted          = "sale_completed";
    public const string SaleCancelled          = "sale_cancelled";
    public const string ManagerAuthorization   = "manager_authorization";

    // Tenants / Billing
    public const string TenantCreated          = "tenant_created";
    public const string ModuleActivated        = "module_activated";
    public const string ModuleDeactivated      = "module_deactivated";
    public const string SubscriptionRenewed    = "subscription_renewed";
    public const string SubscriptionCancelled  = "subscription_cancelled";

    // Platform admin
    public const string PlatformImpersonation  = "platform_impersonation";
    public const string TenantUpdated          = "tenant_updated";
    public const string TenantStatusChanged    = "tenant_status_changed";
}

/// <summary>Audit severity constants.</summary>
public static class AuditSeverity
{
    public const string Info     = "info";
    public const string Warning  = "warning";
    public const string Critical = "critical";
}
