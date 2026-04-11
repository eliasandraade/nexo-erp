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
  modules: string[];
  stores: PlatformStore[];
  userCount: number;
}

export interface PlatformTenantDetail extends PlatformTenant {
  phone?: string;
  businessType?: string;
  subscriptions: Array<{ id: string; moduleKey: string; status: string }>;
  users: Array<{ id: string; name: string; login: string; email: string; role: string; status: string }>;
}
