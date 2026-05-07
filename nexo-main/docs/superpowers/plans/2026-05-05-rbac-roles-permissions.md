# RBAC — Perfis e Permissões de Acesso Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restringir o acesso às rotas e à sidebar de acordo com o papel (role) do usuário logado, e adicionar o papel `Cozinha` para operadores de cozinha no módulo restaurante.

**Architecture:** Adicionar um campo `roles?: UserRole[]` em cada rota da sidebar para declarar quem pode ver aquela entrada. Criar um `RoleRoute` (análogo ao `ModuleRoute` existente) para proteger rotas. A lógica de acesso fica em um hook `useRoleAccess` que é a única fonte de verdade para "qual role pode acessar o quê". O backend recebe `Cozinha` no enum e na validação; nenhuma migration é necessária pois o campo `role` já é armazenado como string.

**Tech Stack:** React + TypeScript + React Router v6 (frontend) · .NET 8 + FluentValidation (backend)

---

## Permission Matrix (fonte da verdade)

| Papel        | Rotas acessíveis                                                                  |
|--------------|-----------------------------------------------------------------------------------|
| `diretoria`  | Todas                                                                             |
| `gerente`    | Todas (filtro de loja já feito pelo JWT/backend)                                  |
| `vendedor`   | Se módulo `varejo`: `/pdv` · Se módulo `restaurante`: `/restaurante`, `/restaurante/delivery`, `/restaurante/mesa/*`, `/restaurante/comanda/*` |
| `estoquista` | `/estoque`, `/estoque/*`, `/produtos`, `/produtos/*`                             |
| `cozinha`    | `/restaurante/cozinha` apenas                                                    |

**Redirect pós-login por role:**

| Papel        | Destino                           |
|--------------|-----------------------------------|
| `diretoria`  | `/dashboard`                      |
| `gerente`    | `/dashboard`                      |
| `vendedor`   | varejo→`/pdv` · restaurante→`/restaurante` |
| `estoquista` | `/estoque`                        |
| `cozinha`    | `/restaurante/cozinha`            |

---

## File Map

| Ação    | Arquivo                                                                        | Responsabilidade                                   |
|---------|--------------------------------------------------------------------------------|----------------------------------------------------|
| Modify  | `nexo-backend/src/Nexo.Domain/Enums/UserRole.cs`                              | Adicionar `Cozinha` ao enum                        |
| Modify  | `nexo-backend/src/Nexo.Application/Features/Users/UserService.cs`             | `ParseRole` + `MapToDto` reconhecem `Cozinha`      |
| Modify  | `nexo-backend/src/Nexo.Application/Validators/Users/CreateUserRequestValidator.cs` | Incluir `"cozinha"` nos roles válidos         |
| Modify  | `nexo-backend/src/Nexo.Api/Program.cs`                                        | Adicionar policy `Cozinha` se necessário           |
| Modify  | `src/modules/users/types/index.ts`                                            | Adicionar `"cozinha"` ao `UserRole`                |
| Create  | `src/modules/auth/hooks/useRoleAccess.ts`                                     | Hook: `homeRoute(session)` + `canAccessPath(role, modules, path)` |
| Create  | `src/app/router/RoleRoute.tsx`                                                 | Route guard baseado em role                        |
| Modify  | `src/app/router/routes.ts`                                                     | Adicionar campo `roles?: UserRole[]` em `AppRoute` |
| Modify  | `src/components/shared/AppSidebar.tsx`                                         | Filtrar sidebar por role além de moduleKey         |
| Modify  | `src/app/router/AppRouter.tsx`                                                 | Envolver rotas sensíveis com `<RoleRoute>`         |
| Modify  | `src/modules/auth/pages/LoginPage.tsx`                                         | Redirecionar para `homeRoute` pós-login            |
| Modify  | `src/modules/auth/context/AuthContext.tsx`                                     | Exportar `homeRoute` via context (opcional, se necessário) |
| Modify  | `src/modules/users/components/UserFormSections.tsx`                            | Adicionar `cozinha` no select de perfil            |

---

### Task 1: Backend — adicionar role `Cozinha`

**Files:**
- Modify: `nexo-backend/src/Nexo.Domain/Enums/UserRole.cs`
- Modify: `nexo-backend/src/Nexo.Application/Features/Users/UserService.cs`
- Modify: `nexo-backend/src/Nexo.Application/Validators/Users/CreateUserRequestValidator.cs`

- [ ] **Step 1: Adicionar `Cozinha` ao enum**

Arquivo: `nexo-backend/src/Nexo.Domain/Enums/UserRole.cs`

```csharp
namespace Nexo.Domain.Enums;

public enum UserRole
{
    Diretoria,
    Gerente,
    Vendedor,
    Estoquista,
    Cozinha,
}
```

- [ ] **Step 2: Atualizar `ParseRole` e `MapToDto` em `UserService.cs`**

Arquivo: `nexo-backend/src/Nexo.Application/Features/Users/UserService.cs`

Na função `ParseRole`:
```csharp
private static UserRole ParseRole(string role) =>
    role.ToLowerInvariant() switch
    {
        "diretoria"  => UserRole.Diretoria,
        "gerente"    => UserRole.Gerente,
        "vendedor"   => UserRole.Vendedor,
        "estoquista" => UserRole.Estoquista,
        "cozinha"    => UserRole.Cozinha,
        _ => throw new DomainException($"Unknown role '{role}'."),
    };
```

- [ ] **Step 3: Adicionar `"cozinha"` nos roles válidos do validator**

Arquivo: `nexo-backend/src/Nexo.Application/Validators/Users/CreateUserRequestValidator.cs`

```csharp
private static readonly string[] ValidRoles =
    ["diretoria", "gerente", "vendedor", "estoquista", "cozinha"];
```

- [ ] **Step 4: Build para confirmar que compila sem erros**

```bash
cd nexo-backend
dotnet build
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
cd nexo-backend
git add src/Nexo.Domain/Enums/UserRole.cs \
        src/Nexo.Application/Features/Users/UserService.cs \
        src/Nexo.Application/Validators/Users/CreateUserRequestValidator.cs
git commit -m "feat(users): adicionar role Cozinha ao backend"
```

---

### Task 2: Frontend — adicionar `"cozinha"` ao tipo `UserRole`

**Files:**
- Modify: `src/modules/users/types/index.ts`
- Modify: `src/modules/users/components/UserFormSections.tsx`

- [ ] **Step 1: Adicionar `"cozinha"` ao union type e labels**

Arquivo: `src/modules/users/types/index.ts`

```typescript
export type UserRole = "diretoria" | "gerente" | "vendedor" | "estoquista" | "cozinha";

// Já existente — adicionar entrada:
export const roleLabels: Record<UserRole, string> = {
  diretoria:  "Diretoria",
  gerente:    "Gerente",
  vendedor:   "Vendedor",
  estoquista: "Estoquista",
  cozinha:    "Cozinha",
};
```

- [ ] **Step 2: Verificar que o TS compila (sem erros de tipo)**

```bash
cd nexo-main
npx tsc --noEmit
```
Expected: nenhum erro relacionado a `UserRole`.

- [ ] **Step 3: Commit**

```bash
cd nexo-main
git add src/modules/users/types/index.ts
git commit -m "feat(users): adicionar role cozinha ao tipo UserRole"
```

---

### Task 3: Hook `useRoleAccess` — fonte da verdade de permissões

**Files:**
- Create: `src/modules/auth/hooks/useRoleAccess.ts`

Este hook centraliza toda a lógica de "quem pode acessar o quê". Qualquer mudança futura de permissão acontece aqui, nunca espalhada em componentes.

- [ ] **Step 1: Criar o hook**

Arquivo: `src/modules/auth/hooks/useRoleAccess.ts`

```typescript
import type { UserRole } from "@/modules/users/types";
import type { AuthSession } from "../types";

/**
 * Returns the home route for a given session (used after login and as fallback redirect).
 */
export function homeRoute(session: AuthSession): string {
  switch (session.role) {
    case "cozinha":
      return "/restaurante/cozinha";
    case "vendedor":
      if (session.modules.includes("varejo"))      return "/pdv";
      if (session.modules.includes("restaurante")) return "/restaurante";
      return "/dashboard";
    case "estoquista":
      return "/estoque";
    case "gerente":
    case "diretoria":
    default:
      return "/dashboard";
  }
}

/**
 * Returns true if the given role + modules combination can access the given path prefix.
 *
 * Rules:
 * - diretoria: all paths
 * - gerente: all paths (store scoping is enforced by the backend/JWT)
 * - vendedor + varejo: /pdv only
 * - vendedor + restaurante: /restaurante (floor/orders/delivery), NOT management pages
 * - estoquista: /estoque and /produtos only
 * - cozinha: /restaurante/cozinha only
 */
export function canAccessPath(
  role: UserRole,
  modules: string[],
  path: string
): boolean {
  if (role === "diretoria" || role === "gerente") return true;

  if (role === "cozinha") {
    return path === "/restaurante/cozinha";
  }

  if (role === "estoquista") {
    return path.startsWith("/estoque") || path.startsWith("/produtos") || path === "/perfil";
  }

  if (role === "vendedor") {
    const hasVarejo      = modules.includes("varejo");
    const hasRestaurante = modules.includes("restaurante");

    if (hasVarejo && path.startsWith("/pdv")) return true;

    if (hasRestaurante) {
      // Allowed: floor, table orders, comanda, delivery, kitchen view
      const allowedPrefixes = [
        "/restaurante/cozinha",
        "/restaurante/delivery",
        "/restaurante/mesa",
        "/restaurante/comanda",
      ];
      if (path === "/restaurante") return true;
      if (allowedPrefixes.some((p) => path.startsWith(p))) return true;
    }

    // Always allow profile
    if (path === "/perfil") return true;

    return false;
  }

  return false;
}

/**
 * Hook version — reads session from context.
 * Import useAuth in component and pass session to the pure functions above when possible.
 */
export function useRoleAccess() {
  // Intentionally thin — consumers call canAccessPath() and homeRoute() directly
  // with session from useAuth(), keeping this hook pure and testable.
  return { canAccessPath, homeRoute };
}
```

- [ ] **Step 2: Verificar que compila**

```bash
cd nexo-main
npx tsc --noEmit
```
Expected: sem erros.

- [ ] **Step 3: Commit**

```bash
cd nexo-main
git add src/modules/auth/hooks/useRoleAccess.ts
git commit -m "feat(auth): hook useRoleAccess com lógica de permissões por role"
```

---

### Task 4: `RoleRoute` — route guard baseado em role

**Files:**
- Create: `src/app/router/RoleRoute.tsx`

- [ ] **Step 1: Criar o componente**

Arquivo: `src/app/router/RoleRoute.tsx`

```typescript
import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { canAccessPath, homeRoute } from "@/modules/auth/hooks/useRoleAccess";

interface RoleRouteProps {
  /**
   * The path prefix this guard protects (e.g. "/pdv", "/restaurante/cozinha").
   * Checked against canAccessPath() using role + modules from the active session.
   */
  path: string;
}

/**
 * Route guard for role-based access.
 *
 * Always runs after ProtectedRoute (session is guaranteed non-null here).
 * If the user's role cannot access `path`, redirects to their home route.
 */
export function RoleRoute({ path }: RoleRouteProps) {
  const { session } = useAuth();

  if (!session) return <Navigate to="/login" replace />;

  if (!canAccessPath(session.role, session.modules, path)) {
    return <Navigate to={homeRoute(session)} replace />;
  }

  return <Outlet />;
}
```

- [ ] **Step 2: Verificar que compila**

```bash
cd nexo-main
npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
cd nexo-main
git add src/app/router/RoleRoute.tsx
git commit -m "feat(router): RoleRoute guard baseado em role do usuário"
```

---

### Task 5: Atualizar `routes.ts` com visibilidade por role

**Files:**
- Modify: `src/app/router/routes.ts`

- [ ] **Step 1: Adicionar campo `roles` ao tipo `AppRoute` e anotar cada rota**

Arquivo: `src/app/router/routes.ts`

```typescript
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
   */
  moduleKey?: string;
  /**
   * If set, the route is only shown to users with one of these roles.
   * Undefined = visible to diretoria and gerente (management roles) only.
   * Use an explicit array to broaden access.
   */
  roles?: UserRole[];
}

/** Roles that can see the full management sidebar */
const MGMT: UserRole[] = ["diretoria", "gerente"];

export const appRoutes: AppRoute[] = [
  // ── Core — management only ──────────────────────────────────────────────
  { path: "/dashboard",     label: "Dashboard",     icon: LayoutDashboard,   roles: MGMT },
  { path: "/vendas",        label: "Vendas",         icon: Receipt,           roles: MGMT },
  { path: "/clientes",      label: "Clientes",       icon: Users,             roles: MGMT },
  { path: "/fornecedores",  label: "Fornecedores",   icon: Truck,             roles: MGMT },
  { path: "/caixa",         label: "Caixa",          icon: Wallet,            roles: MGMT },
  { path: "/usuarios",      label: "Usuários",       icon: UserCog,           roles: ["diretoria"] },
  { path: "/auditoria",     label: "Auditoria",      icon: Shield,            roles: ["diretoria"] },
  { path: "/configuracoes", label: "Configurações",  icon: Settings,          roles: MGMT },

  // ── Estoque — management + estoquista ───────────────────────────────────
  { path: "/produtos",      label: "Produtos",       icon: Package,           roles: [...MGMT, "estoquista"] },
  { path: "/estoque",       label: "Estoque",        icon: Warehouse,         roles: [...MGMT, "estoquista"] },

  // ── Varejo ──────────────────────────────────────────────────────────────
  { path: "/pdv",           label: "PDV",            icon: ShoppingCart,      moduleKey: "varejo",      roles: [...MGMT, "vendedor"] },

  // ── Restaurante — management ─────────────────────────────────────────────
  { path: "/restaurante",            label: "Restaurante",   icon: UtensilsCrossed,   moduleKey: "restaurante", roles: [...MGMT, "vendedor"] },
  { path: "/restaurante/delivery",   label: "Entregas",      icon: Bike,              moduleKey: "restaurante", roles: [...MGMT, "vendedor"] },
  { path: "/restaurante/portal",     label: "Portal",        icon: Globe,             moduleKey: "restaurante", roles: MGMT },
  { path: "/restaurante/configurar", label: "Config. Mesas", icon: SlidersHorizontal, moduleKey: "restaurante", roles: MGMT },
  { path: "/restaurante/relatorios", label: "Relatórios",    icon: BarChart2,         moduleKey: "restaurante", roles: MGMT },

  // ── Cozinha ─────────────────────────────────────────────────────────────
  { path: "/restaurante/cozinha",    label: "Cozinha",       icon: ChefHat,           moduleKey: "restaurante", roles: ["cozinha", ...MGMT] },
];
```

- [ ] **Step 2: Verificar que compila**

```bash
cd nexo-main
npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
cd nexo-main
git add src/app/router/routes.ts
git commit -m "feat(routes): adicionar campo roles em AppRoute para controle de visibilidade"
```

---

### Task 6: Atualizar `AppSidebar` para filtrar por role

**Files:**
- Modify: `src/components/shared/AppSidebar.tsx`

- [ ] **Step 1: Aplicar filtro de role na sidebar**

Arquivo: `src/components/shared/AppSidebar.tsx`

```typescript
import { NavLink } from "react-router-dom";
import { cn } from "@/lib/utils";
import { appRoutes } from "@/app/router/routes";
import { useAuth } from "@/modules/auth/context/AuthContext";

export function AppSidebar() {
  const { session } = useAuth();

  const visibleRoutes = appRoutes.filter((route) => {
    // Module gate
    if (route.moduleKey && !session?.modules.includes(route.moduleKey)) return false;
    // Role gate — undefined roles means visible to all authenticated users
    if (route.roles && session?.role && !route.roles.includes(session.role)) return false;
    return true;
  });

  return (
    <aside className="w-60 min-h-screen bg-sidebar flex flex-col shrink-0">
      {/* Brand */}
      <div className="px-5 pt-6 pb-4">
        <img
          src="/orken_darkmode.png"
          alt="Orken"
          className="h-7 w-auto object-contain"
        />
      </div>

      {/* Navigation */}
      <nav className="flex-1 px-3 py-2 space-y-0.5 overflow-y-auto">
        {visibleRoutes.map((route) => (
          <NavLink
            key={route.path}
            to={route.path}
            className={({ isActive }) =>
              cn(
                "w-full flex items-center gap-3 px-3 py-2 rounded-md text-[13px] font-medium transition-colors",
                isActive
                  ? "bg-sidebar-accent text-sidebar-accent-foreground"
                  : "text-sidebar-foreground hover:bg-sidebar-accent/50 hover:text-sidebar-accent-foreground"
              )
            }
          >
            <route.icon className="h-4 w-4 shrink-0" />
            <span>{route.label}</span>
          </NavLink>
        ))}
      </nav>

      {/* Footer */}
      <div className="px-5 py-4 border-t border-sidebar-border">
        <p className="text-[10px] text-sidebar-muted">
          Andrade Systems © 2026
        </p>
      </div>
    </aside>
  );
}
```

- [ ] **Step 2: Verificar que compila**

```bash
cd nexo-main
npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
cd nexo-main
git add src/components/shared/AppSidebar.tsx
git commit -m "feat(sidebar): filtrar itens de navegação por role do usuário"
```

---

### Task 7: Atualizar `AppRouter` com guards de role

**Files:**
- Modify: `src/app/router/AppRouter.tsx`

A ideia: envolver grupos de rotas com `<RoleRoute path="...">` para que usuários sem permissão sejam redirecionados para a home deles ao tentar acessar diretamente via URL.

- [ ] **Step 1: Importar `RoleRoute` e envolver rotas sensíveis**

Arquivo: `src/app/router/AppRouter.tsx`

Adicionar o import no topo:
```typescript
import { RoleRoute } from "./RoleRoute";
```

Substituir o bloco de "core routes" existente:

```typescript
{/* Protected: core routes — management (diretoria + gerente) */}
<Route element={<ProtectedRoute />}>
  <Route element={<RoleRoute path="/dashboard" />}>
    <Route element={<MainAppLayout />}>
      <Route path="/dashboard"              element={<DashboardPage />} />
      <Route path="/vendas"                 element={<VendasPage />} />
      <Route path="/vendas/:id"             element={<VendaDetailPage />} />
      <Route path="/clientes"               element={<ClientesPage />} />
      <Route path="/clientes/novo"          element={<CustomerFormPage />} />
      <Route path="/clientes/:id"           element={<CustomerFormPage />} />
      <Route path="/fornecedores"           element={<FornecedoresPage />} />
      <Route path="/fornecedores/novo"      element={<SupplierFormPage />} />
      <Route path="/fornecedores/:id"       element={<SupplierFormPage />} />
      <Route path="/caixa"                  element={<CaixaPage />} />
      <Route path="/auditoria"              element={<AuditoriaPage />} />
      <Route path="/configuracoes"          element={<ConfiguracoesPage />} />
    </Route>
  </Route>
</Route>

{/* Protected: usuários — diretoria only */}
<Route element={<ProtectedRoute />}>
  <Route element={<RoleRoute path="/usuarios" />}>
    <Route element={<MainAppLayout />}>
      <Route path="/usuarios"               element={<UsuariosPage />} />
      <Route path="/usuarios/novo"          element={<UserFormPage />} />
      <Route path="/usuarios/:id"           element={<UserFormPage />} />
      <Route path="/usuarios/permissoes"    element={<PermissoesPage />} />
    </Route>
  </Route>
</Route>

{/* Protected: produtos + estoque — management + estoquista */}
<Route element={<ProtectedRoute />}>
  <Route element={<RoleRoute path="/estoque" />}>
    <Route element={<MainAppLayout />}>
      <Route path="/produtos"               element={<ProdutosPage />} />
      <Route path="/produtos/novo"          element={<ProductFormPage />} />
      <Route path="/produtos/:id"           element={<ProductFormPage />} />
      <Route path="/estoque"                element={<EstoquePage />} />
      <Route path="/estoque/movimentacoes"  element={<MovimentacoesPage />} />
      <Route path="/estoque/ajustes"        element={<AjustesPage />} />
    </Route>
  </Route>
</Route>

{/* Protected: perfil — todos os roles */}
<Route element={<ProtectedRoute />}>
  <Route element={<MainAppLayout />}>
    <Route path="/perfil" element={<PerfilPage />} />
  </Route>
</Route>
```

Para o bloco PDV (varejo), adicionar `RoleRoute`:
```typescript
{/* Protected: varejo — PDV */}
<Route element={<ProtectedRoute />}>
  <Route element={<ModuleRoute moduleKey="varejo" />}>
    <Route element={<RoleRoute path="/pdv" />}>
      <Route element={<PosLayout />}>
        <Route path="/pdv" element={<PdvPage />} />
      </Route>
    </Route>
  </Route>
</Route>
```

Para restaurante, separar rotas de operação (vendedor/cozinha) das de gestão (gerente/diretoria):
```typescript
{/* Restaurante — waiter/vendedor pages */}
<Route element={<ProtectedRoute />}>
  <Route element={<ModuleRoute moduleKey="restaurante" />}>
    <Route element={<RoleRoute path="/restaurante" />}>
      <Route element={<WaiterLayout />}>
        <Route path="/restaurante"                  element={<FloorPage />} />
        <Route path="/restaurante/mesa/:tableId"    element={<OrderPage />} />
        <Route path="/restaurante/comanda/:orderId" element={<OrderPage />} />
        <Route path="/restaurante/delivery"         element={<DeliveryPage />} />
      </Route>
    </Route>

    {/* Cozinha — role cozinha (+ diretoria/gerente) */}
    <Route element={<RoleRoute path="/restaurante/cozinha" />}>
      <Route element={<KitchenLayout />}>
        <Route path="/restaurante/cozinha" element={<KitchenPage />} />
      </Route>
    </Route>

    {/* Gestão — diretoria + gerente only */}
    <Route element={<RoleRoute path="/restaurante/portal" />}>
      <Route element={<MainAppLayout />}>
        <Route path="/restaurante/portal"     element={<PortalSetupPage />} />
        <Route path="/restaurante/configurar" element={<RestauranteSetupPage />} />
        <Route path="/restaurante/relatorios" element={<RelatoriosPage />} />
      </Route>
    </Route>
  </Route>
</Route>
```

- [ ] **Step 2: Verificar que compila**

```bash
cd nexo-main
npx tsc --noEmit
```

- [ ] **Step 3: Testar manualmente** — logar como diretoria, conferir que vê tudo. Testar que o TypeScript não reclama.

- [ ] **Step 4: Commit**

```bash
cd nexo-main
git add src/app/router/AppRouter.tsx
git commit -m "feat(router): aplicar guards de role nas rotas protegidas"
```

---

### Task 8: Redirect inteligente pós-login

**Files:**
- Modify: `src/modules/auth/pages/LoginPage.tsx`

- [ ] **Step 1: Usar `homeRoute` para redirecionar após login**

Arquivo: `src/modules/auth/pages/LoginPage.tsx`

Adicionar import:
```typescript
import { homeRoute } from "@/modules/auth/hooks/useRoleAccess";
```

Substituir o bloco após login bem-sucedido (dentro de `handleSubmit`):
```typescript
// Antes:
navigate(type === "platform" ? "/platform" : "/dashboard", { replace: true });

// Depois:
if (type === "platform") {
  navigate("/platform", { replace: true });
} else {
  // Buscar session atualizada do localStorage para determinar home route
  const { getCurrentSession } = await import("../services/authService");
  const s = getCurrentSession();
  navigate(s ? homeRoute(s) : "/dashboard", { replace: true });
}
```

- [ ] **Step 2: Verificar que compila**

```bash
cd nexo-main
npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
cd nexo-main
git add src/modules/auth/pages/LoginPage.tsx
git commit -m "feat(login): redirecionar para home correta por role após login"
```

---

### Task 9: Adicionar `cozinha` no formulário de usuário

**Files:**
- Modify: `src/modules/users/components/UserFormSections.tsx`

> Nota: `roleLabels` já inclui `cozinha` após a Task 2. O select em `UserAccessSection` já itera sobre `Object.keys(roleLabels)`, portanto a opção aparecerá automaticamente. **Esta task só é necessária se o select filtrar roles explicitamente.**

- [ ] **Step 1: Verificar que o select de perfil já exibe `Cozinha`**

Abrir `src/modules/users/components/UserFormSections.tsx` e confirmar que `UserAccessSection` usa:
```typescript
{(Object.keys(roleLabels) as UserRole[]).map((r) => (
  <SelectItem key={r} value={r}>{roleLabels[r]}</SelectItem>
))}
```

Se usar essa forma, nenhuma mudança é necessária — `cozinha` aparece automaticamente.  
Se hardcodar os valores, adicionar `"cozinha"` à lista.

- [ ] **Step 2: Adicionar hint informativo sobre qual role é para qual módulo**

No `UserAccessSection`, abaixo do Select de perfil, adicionar texto de ajuda dinâmico:

```typescript
const roleHints: Partial<Record<UserRole, string>> = {
  vendedor:   "Acessa PDV (varejo) ou piso/delivery (restaurante)",
  estoquista: "Acessa apenas Produtos e Estoque",
  cozinha:    "Acessa apenas a visão de Cozinha (módulo restaurante)",
};

// Dentro do JSX, após o Select de perfil:
{form.role && roleHints[form.role] && (
  <p className="text-xs text-muted-foreground">{roleHints[form.role]}</p>
)}
```

- [ ] **Step 3: Verificar visualmente** — abrir o formulário de novo usuário e conferir que `Cozinha` aparece no select com o hint correspondente.

- [ ] **Step 4: Commit**

```bash
cd nexo-main
git add src/modules/users/components/UserFormSections.tsx
git commit -m "feat(users): hint informativo de acesso por role no formulário"
```

---

### Task 10: Push final e validação em produção

- [ ] **Step 1: Push do frontend**

```bash
cd nexo-main
git push origin master
```

- [ ] **Step 2: Push do backend**

```bash
cd nexo-backend
git push origin master
```

- [ ] **Step 3: Aguardar Railway deploy e validar**

Acessar o sistema com cada tipo de usuário:

| Usuário teste     | Ação                                          | Esperado                              |
|-------------------|-----------------------------------------------|---------------------------------------|
| diretoria         | Login                                         | Redirect → `/dashboard`, sidebar completa |
| gerente           | Login                                         | Redirect → `/dashboard`, sidebar completa |
| vendedor (varejo) | Login                                         | Redirect → `/pdv`, sidebar só mostra PDV |
| vendedor (restaurante) | Login                                    | Redirect → `/restaurante`, sidebar mostra floor/delivery |
| estoquista        | Login                                         | Redirect → `/estoque`, sidebar mostra Produtos + Estoque |
| cozinha           | Login                                         | Redirect → `/restaurante/cozinha`, sidebar mostra só Cozinha |
| qualquer          | Acessar `/dashboard` sem permissão via URL   | Redirect para home do role            |

---

## Spec Coverage Check

- ✅ Diretoria/dono vê tudo → `roles: MGMT` em routes + `diretoria/gerente → return true` em `canAccessPath`
- ✅ Gerente controla a loja toda → mesmo acesso de `diretoria` na UI (filtro de loja é backend/JWT)
- ✅ Vendedor varejo → só PDV → `roles: [...MGMT, "vendedor"]` em `/pdv` + `canAccessPath` restringe
- ✅ Vendedor restaurante → só floor/delivery → `canAccessPath` para vendedor + restaurante
- ✅ Cozinha (novo role) → só `/restaurante/cozinha` → backend + frontend + sidebar + redirect
- ✅ Estoquista → produtos + estoque (comportamento existente preservado)
- ✅ Redirect pós-login inteligente por role
- ✅ Sem migration necessária (role é string no banco)
