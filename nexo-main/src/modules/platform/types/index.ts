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
  status: "healthy" | "unhealthy" | "degraded";
  latencyMs: number;
}

export interface PlatformHealth {
  status: "healthy" | "degraded" | "unhealthy";
  timestamp: string;
  checks: HealthCheck[];
}

// ─── Endpoints ───────────────────────────────────────────────────────────────

export interface ApiEndpoint {
  method: string;
  path: string;
  controller: string;
  action: string;
  description: string;
}

// ─── Impersonate ─────────────────────────────────────────────────────────────

export interface ImpersonateResult {
  accessToken: string;
  refreshToken: string;
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
