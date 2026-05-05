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
  UtensilsCrossed,
  SlidersHorizontal,
  BarChart2,
  Bike,
  Globe,
  ChefHat,
  type LucideIcon,
} from "lucide-react";
import type { UserRole } from "@/modules/users/types";

export interface AppRoute {
  path: string;
  label: string;
  icon: LucideIcon;
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
}

/** Roles that have full management access */
const MGMT: UserRole[] = ["diretoria", "gerente"];

export const appRoutes: AppRoute[] = [
  // ── Core — management only ───────────────────────────────────────────────
  { path: "/dashboard",     label: "Dashboard",     icon: LayoutDashboard,   roles: MGMT },
  { path: "/vendas",        label: "Vendas",         icon: Receipt,           roles: MGMT },
  { path: "/clientes",      label: "Clientes",       icon: Users,             roles: MGMT },
  { path: "/fornecedores",  label: "Fornecedores",   icon: Truck,             roles: MGMT },
  { path: "/caixa",         label: "Caixa",          icon: Wallet,            roles: MGMT },
  { path: "/usuarios",      label: "Usuários",       icon: UserCog,           roles: ["diretoria"] },
  { path: "/auditoria",     label: "Auditoria",      icon: Shield,            roles: ["diretoria"] },
  { path: "/configuracoes", label: "Configurações",  icon: Settings,          roles: MGMT },

  // ── Estoque — management + estoquista ────────────────────────────────────
  { path: "/produtos",      label: "Produtos",       icon: Package,           roles: [...MGMT, "estoquista"] },
  { path: "/estoque",       label: "Estoque",        icon: Warehouse,         roles: [...MGMT, "estoquista"] },

  // ── Varejo ───────────────────────────────────────────────────────────────
  { path: "/pdv",           label: "PDV",            icon: ShoppingCart,      moduleKey: "varejo",      roles: [...MGMT, "vendedor"] },

  // ── Restaurante — operação (vendedor) e gestão (gerente/diretoria) ───────
  { path: "/restaurante",            label: "Restaurante",   icon: UtensilsCrossed,   moduleKey: "restaurante", roles: [...MGMT, "vendedor"] },
  { path: "/restaurante/delivery",   label: "Entregas",      icon: Bike,              moduleKey: "restaurante", roles: [...MGMT, "vendedor"] },
  { path: "/restaurante/portal",     label: "Portal",        icon: Globe,             moduleKey: "restaurante", roles: MGMT },
  { path: "/restaurante/configurar", label: "Config. Mesas", icon: SlidersHorizontal, moduleKey: "restaurante", roles: MGMT },
  { path: "/restaurante/relatorios", label: "Relatórios",    icon: BarChart2,         moduleKey: "restaurante", roles: MGMT },

  // ── Cozinha — role exclusivo + management pode acessar também ────────────
  { path: "/restaurante/cozinha",    label: "Cozinha",       icon: ChefHat,           moduleKey: "restaurante", roles: [...MGMT, "cozinha"] },
];
