// ─── Base tenant types ────────────────────────────────────────────────────────

export interface PlatformStore {
  id: string;
  name: string;
  slug: string;
  status: string;
}

export interface PlatformTenant {
  id: string;
  companyName: string;
  tradeName?: string;
  slug: string;
  status: string;
  email: string;
  taxId: string;
  phone?: string;
  businessType?: string;
  createdAt: string;
  modules: string[];
  stores: PlatformStore[];
  userCount: number;
}

export interface PlatformSubscription {
  id: string;
  moduleKey: string;
  status: string;
  planType: string;
  currentPeriodEnd?: string;
  cancelAtPeriodEnd: boolean;
}

export interface PlatformUser {
  id: string;
  name: string;
  login: string;
  email: string;
  role: string;
  status: string;
  lastAccessAt?: string;
}

export interface PlatformTenantDetail extends PlatformTenant {
  trialEndsAt?: string;
  subscriptions: PlatformSubscription[];
  users: PlatformUser[];
}

// ─── Create tenant ────────────────────────────────────────────────────────────

export interface CreateTenantInput {
  companyName: string;
  taxId: string;
  email: string;
  tradeName?: string;
  phone?: string;
  businessType?: string;
  modules: string[];
  adminName: string;
  adminLogin: string;
  adminPassword: string;
  adminEmail?: string;
}

// ─── Stats ────────────────────────────────────────────────────────────────────

export interface PlatformStats {
  tenantCount: number;
  activeCount: number;
  suspendedCount: number;
  storeCount: number;
  userCount: number;
  activeSubscriptions: number;
  moduleBreakdown: { moduleKey: string; count: number }[];
  recentTenants: {
    id: string;
    companyName: string;
    tradeName?: string;
    status: string;
    createdAt: string;
    email: string;
  }[];
}

// ─── Health ───────────────────────────────────────────────────────────────────

export interface HealthCheck {
  name: string;
  /** not_configured: the piece is not set up in this deployment (e.g. no cache server) — neither good nor bad. */
  status: "healthy" | "unhealthy" | "not_configured";
  latencyMs: number;
}

export interface PlatformHealth {
  status: "healthy" | "degraded";
  timestamp: string;
  checks: HealthCheck[];
}

// ─── Endpoints ───────────────────────────────────────────────────────────────

export interface ApiEndpoint {
  method: string;
  /** Full route, already starting with "/api/…". */
  path: string;
  /** The route has {parameters}: it cannot be called as it is. */
  hasParameters: boolean;
  controller: string;
  action: string;
  description: string;
}

// ─── Impersonate ─────────────────────────────────────────────────────────────

export interface ImpersonateResult {
  accessToken: string;
  /** Never a refresh token: the access ends at expiresAt. */
  refreshToken: null;
  /** When the access ends (30 minutes). */
  expiresAt: string;
  session: {
    userId: string;
    tenantId: string;
    companyName: string;
    name: string;
    login: string;
    email: string;
    role: string;
    storeId: string;
    storeIds: string[];
    activeModules: string[];
    type: "tenant";
  };
}

// ─── Security of the platform administrator ─────────────────────────────────

export interface PlatformMfaStatus {
  enabled: boolean;
  setupPending: boolean;
  recoveryCodesLeft: number;
}

export interface PlatformSession {
  id: string;
  createdAt: string;
  lastUsedAt: string;
  expiresAt: string;
  mfaVerified: boolean;
  ipAddress: string | null;
  userAgent: string | null;
  /** The session making this request. */
  current: boolean;
}

export interface PlatformSecurity {
  mfa: PlatformMfaStatus;
  /** This sign-in passed two-step verification (required for administrative acts). */
  currentSessionMfaVerified: boolean;
  sessions: PlatformSession[];
}

export interface PlatformMfaSetup {
  /** Base32 key to type in the authenticator app. */
  secret: string;
  otpauthUri: string;
}
