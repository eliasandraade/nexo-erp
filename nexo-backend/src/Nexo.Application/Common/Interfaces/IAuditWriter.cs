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
    public const string PlatformImpersonationEnded = "platform_impersonation_ended";
    public const string PlatformLoggedIn       = "platform_logged_in";
    public const string PlatformLoginFailed    = "platform_login_failed";
    public const string PlatformLoggedOut      = "platform_logged_out";
    public const string PlatformSessionRevoked = "platform_session_revoked";
    public const string PlatformMfaEnabled     = "platform_mfa_enabled";
    public const string PlatformMfaDisabled    = "platform_mfa_disabled";
    public const string PlatformMfaFailed      = "platform_mfa_failed";
    public const string PlatformNoteCreated    = "platform_note_created";
    public const string PlatformNoteDeleted    = "platform_note_deleted";
    public const string PlatformNotePinned     = "platform_note_pinned";

    // AI providers (platform-wide)
    public const string AiProviderUpdated      = "ai_provider_updated";
    public const string AiProviderKeySet       = "ai_provider_key_set";
    public const string AiProviderKeyRemoved   = "ai_provider_key_removed";
    public const string AiProviderTested       = "ai_provider_tested";
    public const string AiTenantLimitsChanged  = "ai_tenant_limits_changed";
    public const string AiPromptCreated        = "ai_prompt_created";
    public const string AiPromptActivated      = "ai_prompt_activated";
    // Orken Build — progress, cost per stage, client portal
    public const string BuildScheduleFrozen          = "build_schedule_frozen";
    public const string BuildMovementStageAssigned   = "build_movement_stage_assigned";
    public const string BuildPortalCompanyUpdated    = "build_portal_company_updated";
    public const string BuildPortalUpdated           = "build_portal_updated";
    public const string BuildPortalPasswordSet       = "build_portal_password_set";
    public const string BuildPortalPasswordRemoved   = "build_portal_password_removed";
    public const string BuildPortalPublished         = "build_portal_published";
    public const string BuildPortalUnpublished       = "build_portal_unpublished";
    public const string BuildPortalLocked            = "build_portal_locked";
    public const string BuildDailyLogClientVisibility = "build_daily_log_client_visibility";
    public const string BuildPhotoClientVisibility   = "build_photo_client_visibility";
    public const string BuildClientUpdateCreated     = "build_client_update_created";
    public const string BuildClientUpdateEdited      = "build_client_update_edited";
    public const string BuildClientUpdatePublished   = "build_client_update_published";
    public const string BuildClientUpdateArchived    = "build_client_update_archived";
    // Orken Build — Wave 2 commercial
    public const string BuildBudgetTermsSet          = "build_budget_terms_set";
    public const string BuildBudgetDuplicated        = "build_budget_duplicated";
    public const string BuildBudgetVersionCreated    = "build_budget_version_created";
    public const string BuildBudgetTemplateSaved     = "build_budget_template_saved";
    public const string BuildBudgetTemplateDeleted   = "build_budget_template_deleted";
    public const string BuildBudgetFromTemplate      = "build_budget_from_template";
    public const string BuildChangeOrderSaved        = "build_change_order_saved";
    public const string BuildChangeOrderSent         = "build_change_order_sent";
    public const string BuildChangeOrderApproved     = "build_change_order_approved";
    public const string BuildChangeOrderRejected     = "build_change_order_rejected";
    public const string BuildChangeOrderCancelled    = "build_change_order_cancelled";
    public const string BuildSelectionSaved          = "build_selection_saved";
    public const string BuildSelectionChosen         = "build_selection_chosen";
    public const string BuildSelectionConfirmed      = "build_selection_confirmed";
    public const string BuildSelectionCancelled      = "build_selection_cancelled";
    public const string BuildMaterialSaved           = "build_material_saved";
    public const string BuildMaterialStatusChanged   = "build_material_status_changed";
    public const string BuildMaterialDelivered       = "build_material_delivered";
    public const string BuildMaterialMovementLinked  = "build_material_movement_linked";
    public const string BuildMeasurementSaved        = "build_measurement_saved";
    public const string BuildMeasurementSubmitted    = "build_measurement_submitted";
    public const string BuildMeasurementApproved     = "build_measurement_approved";
    public const string BuildMeasurementRejected     = "build_measurement_rejected";
    // Orken Build — Wave 3 quality and post-construction
    public const string BuildChecklistTemplateSaved   = "build_checklist_template_saved";
    public const string BuildChecklistTemplateDeleted = "build_checklist_template_deleted";
    public const string BuildInspectionSaved          = "build_inspection_saved";
    public const string BuildInspectionCompleted      = "build_inspection_completed";
    public const string BuildInspectionCancelled      = "build_inspection_cancelled";
    public const string BuildNonConformitySaved       = "build_non_conformity_saved";
    public const string BuildNonConformityStatusChanged = "build_non_conformity_status_changed";
    public const string BuildHandoverSaved            = "build_handover_saved";
    public const string BuildHandoverDelivered        = "build_handover_delivered";
    public const string BuildHandoverAccepted         = "build_handover_accepted";
    public const string BuildWarrantyTermSaved        = "build_warranty_term_saved";
    public const string BuildWarrantyTermDeleted      = "build_warranty_term_deleted";
    public const string BuildAssistanceOpened         = "build_assistance_opened";
    public const string BuildAssistanceStatusChanged  = "build_assistance_status_changed";
    public const string BuildAssistancePhotoAdded     = "build_assistance_photo_added";
    public const string BuildAssistanceFeedback       = "build_assistance_feedback";

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

/// <summary>
/// Claims of a tenant session opened by a platform administrator (impersonation): who really acts, and the marker that
/// the session is an impersonation (no refresh, no account changes, every audit record names the administrator).
/// </summary>
public static class ImpersonationClaims
{
    public const string Marker     = "imp";
    public const string ActorId    = "imp_actor_id";
    public const string ActorEmail = "imp_actor_email";
    public const string Reason     = "imp_reason";
}
