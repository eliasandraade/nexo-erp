import {
  LayoutDashboard,
  ShoppingCart,
  Receipt,
  Package,
  Warehouse,
  Users,
  Truck,
  UserCog,
  Wallet,
  Shield,
  Settings,
  CreditCard,
  UtensilsCrossed,
  SlidersHorizontal,
  BarChart2,
  TrendingUp,
  Bike,
  Globe,
  ChefHat,
  HardHat,
  ConciergeBell,
  BookMarked,
  Boxes,
  CalendarClock,
  type LucideIcon,
} from "lucide-react";
import type { UserRole } from "@/modules/users/types";
import type { ServiceCapabilities } from "@/modules/service/api/service.api";

export type RouteGroup = "core" | "inventario" | "varejo" | "restaurante" | "build" | "service" | "admin";

export interface AppRoute {
  path: string;
  label: string;
  icon: LucideIcon;
  group: RouteGroup;
  /**
   * If set, the route is only shown when `session.modules` includes this key.
   * Undefined = no module gate.
   */
  moduleKey?: string;
  /**
   * If set, the route is only shown to users with one of these roles.
   * Undefined = shown to all authenticated users (use sparingly).
   */
  roles?: UserRole[];
  /**
   * Service-only: the route is shown only when the resolved preset has this capability on
   * (decision D2). Undefined = always shown for the group. The "service" group is gated as a
   * family in the sidebar, not via `moduleKey`.
   */
  capability?: keyof ServiceCapabilities;
}

/** Roles that have full management access */
const MGMT: UserRole[] = ["diretoria", "gerente"];

export const appRoutes: AppRoute[] = [
  // ── Core — management only ───────────────────────────────────────────────
  { path: "/dashboard",     label: "Dashboard",     icon: LayoutDashboard,   group: "core",        roles: MGMT },
  { path: "/vendas",        label: "Vendas",         icon: Receipt,           group: "core",        roles: MGMT },
  { path: "/clientes",      label: "Clientes",       icon: Users,             group: "core",        roles: MGMT },
  { path: "/fornecedores",  label: "Fornecedores",   icon: Truck,             group: "core",        roles: MGMT },
  { path: "/caixa",         label: "Caixa",          icon: Wallet,            group: "core",        roles: MGMT },

  // ── Estoque — management + estoquista ────────────────────────────────────
  { path: "/produtos",      label: "Produtos",       icon: Package,           group: "inventario",  roles: [...MGMT, "estoquista"] },
  { path: "/estoque",       label: "Estoque",        icon: Warehouse,         group: "inventario",  roles: [...MGMT, "estoquista"] },

  // ── Varejo ───────────────────────────────────────────────────────────────
  { path: "/pdv",           label: "PDV",            icon: ShoppingCart,      group: "varejo",      moduleKey: "varejo",      roles: [...MGMT, "vendedor"] },

  // ── Restaurante — operação (vendedor) e gestão (gerente/diretoria) ───────
  { path: "/restaurante",            label: "Salão",         icon: UtensilsCrossed,   group: "restaurante", moduleKey: "restaurante", roles: [...MGMT, "vendedor"] },
  { path: "/restaurante/delivery",   label: "Entregas",      icon: Bike,              group: "restaurante", moduleKey: "restaurante", roles: [...MGMT, "vendedor"] },
  { path: "/restaurante/cozinha",    label: "Cozinha",       icon: ChefHat,           group: "restaurante", moduleKey: "restaurante", roles: [...MGMT, "cozinha"] },
  { path: "/restaurante/portal",     label: "Cardápio online", icon: Globe,           group: "restaurante", moduleKey: "restaurante", roles: MGMT },
  { path: "/restaurante/configurar", label: "Mesas e áreas", icon: SlidersHorizontal, group: "restaurante", moduleKey: "restaurante", roles: MGMT },
  { path: "/restaurante/relatorios", label: "Relatórios",    icon: BarChart2,         group: "restaurante", moduleKey: "restaurante", roles: MGMT },
  { path: "/restaurante/financeiro", label: "Financeiro",    icon: TrendingUp,        group: "restaurante", moduleKey: "restaurante", roles: MGMT },

  // ── Build (Obras) — management only ─────────────────────────────────────
  { path: "/build",                  label: "Obras",         icon: HardHat,           group: "build",       moduleKey: "build",       roles: MGMT },

  // ── Service (Serviços) — management only; family-gated + capability-driven in the sidebar ──
  { path: "/service",                label: "Visão geral",   icon: ConciergeBell,     group: "service",     roles: MGMT },
  { path: "/service/agenda",         label: "Agenda",        icon: CalendarClock,     group: "service",     roles: MGMT, capability: "appointments" },
  { path: "/service/profissionais",  label: "Profissionais", icon: Users,             group: "service",     roles: MGMT },
  { path: "/service/catalogo",       label: "Catálogo",      icon: BookMarked,        group: "service",     roles: MGMT },
  { path: "/service/subjects",       label: "Cadastros",     icon: Boxes,             group: "service",     roles: MGMT, capability: "subjectKind" },

  // ── Admin — diretoria only ───────────────────────────────────────────────
  { path: "/usuarios",      label: "Usuários",       icon: UserCog,           group: "admin",       roles: ["diretoria"] },
  { path: "/auditoria",     label: "Auditoria",      icon: Shield,            group: "admin",       roles: ["diretoria"] },
  { path: "/configuracoes", label: "Configurações",  icon: Settings,          group: "admin",       roles: MGMT },
  { path: "/assinatura",    label: "Assinatura",     icon: CreditCard,        group: "admin",       roles: MGMT },
];
