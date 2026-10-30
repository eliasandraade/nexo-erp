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
    public const string UserLoginFailed        = "user_login_failed";
    public const string UserLoginLocked        = "user_login_locked";
    public const string UserInvited            = "user_invited";
    public const string UserMfaEnabled         = "user_mfa_enabled";
    public const string UserMfaDisabled        = "user_mfa_disabled";
    public const string UserMfaFailed          = "user_mfa_failed";

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
    public const string SaleReturned           = "sale_returned";
    public const string PurchaseReceived       = "purchase_received";
    public const string PurchaseCancelled      = "purchase_cancelled";
    public const string ManagerAuthorization   = "manager_authorization";

    // Tenants / Billing
    public const string TenantCreated          = "tenant_created";
    public const string ModuleActivated        = "module_activated";
    public const string ModuleDeactivated      = "module_deactivated";
    public const string SubscriptionRenewed    = "subscription_renewed";
    public const string SubscriptionCancelled  = "subscription_cancelled";

    // Platform admin
    public const string PlatformImpersonation  = "platform_impersonation";

    // AI providers (platform-wide)
    public const string AiProviderUpdated      = "ai_provider_updated";
    public const string AiProviderKeySet       = "ai_provider_key_set";
    public const string AiProviderKeyRemoved   = "ai_provider_key_removed";
    public const string AiProviderTested       = "ai_provider_tested";
    public const string AiTenantLimitsChanged  = "ai_tenant_limits_changed";
    public const string AiPromptCreated        = "ai_prompt_created";
    public const string AiPromptActivated      = "ai_prompt_activated";
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
