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
  type LucideIcon,
} from "lucide-react";

export interface AppRoute {
  path: string;
  label: string;
  icon: LucideIcon;
  /**
   * If set, the route is only shown when `session.modules` includes this key.
   * Undefined = always visible to authenticated users (core routes).
   */
  moduleKey?: string;
}

export const appRoutes: AppRoute[] = [
  // ── Core — always visible ────────────────────────────────────────────────
  { path: "/dashboard",    label: "Dashboard",     icon: LayoutDashboard },
  { path: "/vendas",       label: "Vendas",        icon: Receipt },
  { path: "/produtos",     label: "Produtos",      icon: Package },
  { path: "/estoque",      label: "Estoque",       icon: Warehouse },
  { path: "/clientes",     label: "Clientes",      icon: Users },
  { path: "/fornecedores", label: "Fornecedores",  icon: Truck },
  { path: "/caixa",        label: "Caixa",         icon: Wallet },
  { path: "/usuarios",     label: "Usuários",      icon: UserCog },
  { path: "/auditoria",    label: "Auditoria",     icon: Shield },
  { path: "/configuracoes",label: "Configurações", icon: Settings },

  // ── Varejo ───────────────────────────────────────────────────────────────
  { path: "/pdv",          label: "PDV",           icon: ShoppingCart, moduleKey: "varejo" },
];
