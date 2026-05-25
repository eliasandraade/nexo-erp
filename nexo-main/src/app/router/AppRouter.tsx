import { lazy, Suspense } from "react";
import { BrowserRouter, Routes, Route, Navigate, useParams } from "react-router-dom";
import { AuthProvider } from "@/modules/auth/context/AuthContext";
import { AuthLayout } from "@/app/layouts/AuthLayout";
import { MainAppLayout } from "@/app/layouts/MainAppLayout";
import { PosLayout } from "@/app/layouts/PosLayout";
import { PlatformLayout } from "@/app/layouts/PlatformLayout";
import { ProtectedRoute } from "./ProtectedRoute";
import { ModuleRoute } from "./ModuleRoute";
import { PlatformRoute } from "./PlatformRoute";
import { RoleRoute } from "./RoleRoute";

// ── Static imports: auth pages only — absolute critical path ─────────────────
// Login/Register must render without a spinner (auth redirect lands here).
import LoginPage      from "@/modules/auth/pages/LoginPage";
import RegisterPage   from "@/modules/auth/pages/RegisterPage";
import CheckEmailPage from "@/modules/auth/pages/CheckEmailPage";
import VerifyEmailPage from "@/modules/auth/pages/VerifyEmailPage";
import NotFoundPage   from "@/pages/NotFoundPage";

// ── Lazy imports: everything else ────────────────────────────────────────────
// Public pages that aren't on the auth critical path (landing, portal) +
// all authenticated app pages. Each group becomes its own chunk.

// Public — lazy because app users never visit these
const LandingPage        = lazy(() => import("@/modules/landing/pages/LandingPage"));
const PortalMenuPage     = lazy(() => import("@/modules/portal/pages/PortalMenuPage"));
const PortalTrackingPage = lazy(() => import("@/modules/portal/pages/PortalTrackingPage"));

const ImpersonatePage       = lazy(() => import("@/pages/ImpersonatePage"));
const PerfilPage            = lazy(() => import("@/modules/profile/pages/PerfilPage"));

// Core / dashboard
const DashboardPage         = lazy(() => import("@/modules/dashboard/pages/DashboardPage"));
const VendasPage            = lazy(() => import("@/modules/sales/pages/VendasPage"));
const VendaDetailPage       = lazy(() => import("@/modules/sales/pages/VendaDetailPage"));
const ProdutosPage          = lazy(() => import("@/modules/products/pages/ProdutosPage"));
const ProductFormPage       = lazy(() => import("@/modules/products/pages/ProductFormPage"));
const EstoquePage           = lazy(() => import("@/modules/inventory/pages/EstoquePage"));
const MovimentacoesPage     = lazy(() => import("@/modules/inventory/pages/MovimentacoesPage"));
const AjustesPage           = lazy(() => import("@/modules/inventory/pages/AjustesPage"));
const ClientesPage          = lazy(() => import("@/modules/customers/pages/ClientesPage"));
const CustomerFormPage      = lazy(() => import("@/modules/customers/pages/CustomerFormPage"));
const FornecedoresPage      = lazy(() => import("@/modules/suppliers/pages/FornecedoresPage"));
const SupplierFormPage      = lazy(() => import("@/modules/suppliers/pages/SupplierFormPage"));
const UsuariosPage          = lazy(() => import("@/modules/users/pages/UsuariosPage"));
const UserFormPage          = lazy(() => import("@/modules/users/pages/UserFormPage"));
const PermissoesPage        = lazy(() => import("@/modules/users/pages/PermissoesPage"));
const CaixaPage             = lazy(() => import("@/modules/cash/pages/CaixaPage"));
const AuditoriaPage         = lazy(() => import("@/modules/audit/pages/AuditoriaPage"));
const ConfiguracoesPage     = lazy(() => import("@/modules/settings/pages/ConfiguracoesPage"));

// PDV (varejo)
const PdvPage               = lazy(() => import("@/modules/sales/pages/PdvPage"));

// Build (obras)
const BuildProjectsPage     = lazy(() => import("@/modules/build/pages/BuildProjectsPage"));
const BuildProjectDetailPage = lazy(() => import("@/modules/build/pages/BuildProjectDetailPage"));

// Restaurante
import { WaiterLayout }     from "@/app/layouts/WaiterLayout";
import { KitchenLayout }    from "@/app/layouts/KitchenLayout";
const FloorPage             = lazy(() => import("@/modules/restaurante/pages/FloorPage"));
const OrderPage             = lazy(() => import("@/modules/restaurante/pages/OrderPage"));
const KitchenPage           = lazy(() => import("@/modules/restaurante/pages/KitchenPage"));
const RestauranteSetupPage  = lazy(() => import("@/modules/restaurante/pages/RestauranteSetupPage"));
const RelatoriosPage        = lazy(() => import("@/modules/restaurante/pages/RelatoriosPage"));
const FinanceiroPage        = lazy(() => import("@/modules/restaurante/pages/FinanceiroPage"));
const DeliveryPage          = lazy(() => import("@/modules/restaurante/pages/DeliveryPage"));
const RecipeCardPage        = lazy(() => import("@/modules/restaurante/pages/RecipeCardPage"));
const PortalSetupPage       = lazy(() => import("@/modules/restaurante/pages/PortalSetupPage"));

// Platform admin
const PlatformDashboardPage    = lazy(() => import("@/modules/platform/pages/PlatformDashboardPage"));
const PlatformTenantsPage      = lazy(() => import("@/modules/platform/pages/PlatformTenantsPage"));
const PlatformTenantDetailPage = lazy(() => import("@/modules/platform/pages/PlatformTenantDetailPage"));
const PlatformSystemPage       = lazy(() => import("@/modules/platform/pages/PlatformSystemPage"));
const PlatformActivityPage     = lazy(() => import("@/modules/platform/pages/PlatformActivityPage"));
const PlatformTrialPage        = lazy(() => import("@/modules/platform/pages/PlatformTrialPage"));
const PlatformFlagsPage        = lazy(() => import("@/modules/platform/pages/PlatformFlagsPage"));
const AiDashboardPage          = lazy(() => import("@/modules/platform/pages/ai/AiDashboardPage"));
const AiPlaygroundPage         = lazy(() => import("@/modules/platform/pages/ai/AiPlaygroundPage"));
const AiProvidersPage          = lazy(() => import("@/modules/platform/pages/ai/AiProvidersPage"));
const AiTelemetryPage          = lazy(() => import("@/modules/platform/pages/ai/AiTelemetryPage"));
const AiCostsPage              = lazy(() => import("@/modules/platform/pages/ai/AiCostsPage"));
const AiPromptsPage            = lazy(() => import("@/modules/platform/pages/ai/AiPromptsPage"));

// ── Route fallback ────────────────────────────────────────────────────────────
// Minimal spinner shown while a lazy chunk downloads (~50ms on warm cache).
function PageLoader() {
  return (
    <div className="flex h-screen items-center justify-center bg-background">
      <div className="h-6 w-6 animate-spin rounded-full border-2 border-muted border-t-primary" />
    </div>
  );
}

/** Redirects legacy /menu/:slug links to /:slug */
function MenuSlugRedirect() {
  const { slug } = useParams<{ slug: string }>();
  return <Navigate to={`/${slug ?? ""}`} replace />;
}

/**
 * AuthProvider lives inside BrowserRouter so it can call useNavigate() for
 * the logout redirect. Route guards are nested in order:
 *   ProtectedRoute → verifies authentication (waits for isReady)
 *   ModuleRoute    → verifies module subscription
 *   RoleRoute      → verifies role-based access, redirects to homeRoute on failure
 */
export function AppRouter() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Suspense fallback={<PageLoader />}>
          <Routes>
            {/* Public: landing page */}
            <Route path="/" element={<LandingPage />} />

            {/* Public: restaurant portal — slug at root */}
            <Route path="/:slug"           element={<PortalMenuPage />} />
            <Route path="/rastrear/:token" element={<PortalTrackingPage />} />

            {/* Legacy: redirect old /menu/:slug links to /:slug */}
            <Route path="/menu/:slug" element={<MenuSlugRedirect />} />

            {/* Public: impersonation entry (opened in new tab by platform admin) */}
            <Route path="/impersonate" element={<ImpersonatePage />} />

            {/* Public: auth pages */}
            <Route element={<AuthLayout />}>
              <Route path="/login"        element={<LoginPage />} />
              <Route path="/register"     element={<RegisterPage />} />
              <Route path="/check-email"  element={<CheckEmailPage />} />
              <Route path="/verify-email" element={<VerifyEmailPage />} />
            </Route>

            {/* ── Varejo: PDV — vendedor + management ────────────────────────── */}
            <Route element={<ProtectedRoute />}>
              <Route element={<ModuleRoute moduleKey="varejo" />}>
                <Route element={<RoleRoute path="/pdv" />}>
                  <Route element={<PosLayout />}>
                    <Route path="/pdv" element={<PdvPage />} />
                  </Route>
                </Route>
              </Route>
            </Route>

            {/* ── Build (Obras) — management only ───────────────────────────────── */}
            <Route element={<ProtectedRoute />}>
              <Route element={<ModuleRoute moduleKey="build" />}>
                <Route element={<RoleRoute path="/build" />}>
                  <Route element={<MainAppLayout />}>
                    <Route path="/build"              element={<BuildProjectsPage />} />
                    <Route path="/build/projetos/:id" element={<BuildProjectDetailPage />} />
                  </Route>
                </Route>
              </Route>
            </Route>

            {/* ── Restaurante: operação — vendedor + cozinha + management ──────── */}
            <Route element={<ProtectedRoute />}>
              <Route element={<ModuleRoute moduleKey="restaurante" />}>

                {/* Floor + waiter routes — vendedor + management */}
                <Route element={<RoleRoute path="/restaurante" />}>
                  <Route element={<WaiterLayout />}>
                    <Route path="/restaurante"                  element={<FloorPage />} />
                    <Route path="/restaurante/mesa/:tableId"    element={<OrderPage />} />
                    <Route path="/restaurante/comanda/:orderId" element={<OrderPage />} />
                    <Route path="/restaurante/delivery"         element={<DeliveryPage />} />
                  </Route>
                </Route>

                {/* Cozinha — role cozinha + management */}
                <Route element={<RoleRoute path="/restaurante/cozinha" />}>
                  <Route element={<KitchenLayout />}>
                    <Route path="/restaurante/cozinha" element={<KitchenPage />} />
                  </Route>
                </Route>

                {/* Gestão restaurante — management only */}
                <Route element={<RoleRoute path="/restaurante/portal" />}>
                  <Route element={<MainAppLayout />}>
                    <Route path="/restaurante/portal"     element={<PortalSetupPage />} />
                    <Route path="/restaurante/configurar" element={<RestauranteSetupPage />} />
                    <Route path="/restaurante/relatorios" element={<RelatoriosPage />} />
                    <Route path="/restaurante/financeiro" element={<FinanceiroPage />} />
                  </Route>
                </Route>

              </Route>
            </Route>

            {/* ── Dashboard + core management — diretoria + gerente ──────────── */}
            <Route element={<ProtectedRoute />}>
              <Route element={<RoleRoute path="/dashboard" />}>
                <Route element={<MainAppLayout />}>
                  <Route path="/dashboard"  element={<DashboardPage />} />
                  <Route path="/vendas"     element={<VendasPage />} />
                  <Route path="/vendas/:id" element={<VendaDetailPage />} />
                  <Route path="/clientes"       element={<ClientesPage />} />
                  <Route path="/clientes/novo"  element={<CustomerFormPage />} />
                  <Route path="/clientes/:id"   element={<CustomerFormPage />} />
                  <Route path="/fornecedores"       element={<FornecedoresPage />} />
                  <Route path="/fornecedores/novo"  element={<SupplierFormPage />} />
                  <Route path="/fornecedores/:id"   element={<SupplierFormPage />} />
                  <Route path="/caixa"         element={<CaixaPage />} />
                  <Route path="/configuracoes" element={<ConfiguracoesPage />} />
                </Route>
              </Route>
            </Route>

            {/* ── Usuários + Auditoria — diretoria only ──────────────────────── */}
            <Route element={<ProtectedRoute />}>
              <Route element={<RoleRoute path="/usuarios" />}>
                <Route element={<MainAppLayout />}>
                  <Route path="/usuarios"            element={<UsuariosPage />} />
                  <Route path="/usuarios/novo"       element={<UserFormPage />} />
                  <Route path="/usuarios/:id"        element={<UserFormPage />} />
                  <Route path="/usuarios/permissoes" element={<PermissoesPage />} />
                  <Route path="/auditoria"           element={<AuditoriaPage />} />
                </Route>
              </Route>
            </Route>

            {/* ── Estoque + Produtos — management + estoquista ───────────────── */}
            <Route element={<ProtectedRoute />}>
              <Route element={<RoleRoute path="/estoque" />}>
                <Route element={<MainAppLayout />}>
                  <Route path="/produtos"              element={<ProdutosPage />} />
                  <Route path="/produtos/novo"         element={<ProductFormPage />} />
                  <Route path="/produtos/:id"          element={<ProductFormPage />} />
                  <Route path="/produtos/:id/ficha"    element={<RecipeCardPage />} />
                  <Route path="/estoque"               element={<EstoquePage />} />
                  <Route path="/estoque/movimentacoes" element={<MovimentacoesPage />} />
                  <Route path="/estoque/ajustes"       element={<AjustesPage />} />
                </Route>
              </Route>
            </Route>

            {/* ── Perfil — todos os roles autenticados ───────────────────────── */}
            <Route element={<ProtectedRoute />}>
              <Route element={<MainAppLayout />}>
                <Route path="/perfil" element={<PerfilPage />} />
              </Route>
            </Route>

            {/* Platform admin — requires type: "platform" in session */}
            <Route element={<PlatformRoute />}>
              <Route element={<PlatformLayout />}>
                <Route path="/platform"                          element={<PlatformDashboardPage />} />
                <Route path="/platform/tenants"                  element={<PlatformTenantsPage />} />
                <Route path="/platform/tenants/:tenantId"        element={<PlatformTenantDetailPage />} />
                <Route path="/platform/trial"                    element={<PlatformTrialPage />} />
                <Route path="/platform/activity"                 element={<PlatformActivityPage />} />
                <Route path="/platform/system"                   element={<PlatformSystemPage />} />
                <Route path="/platform/flags"                    element={<PlatformFlagsPage />} />
                {/* AI Operations */}
                <Route path="/platform/ai"                       element={<AiDashboardPage />} />
                <Route path="/platform/ai/playground"            element={<AiPlaygroundPage />} />
                <Route path="/platform/ai/providers"             element={<AiProvidersPage />} />
                <Route path="/platform/ai/telemetry"             element={<AiTelemetryPage />} />
                <Route path="/platform/ai/costs"                 element={<AiCostsPage />} />
                <Route path="/platform/ai/prompts"               element={<AiPromptsPage />} />
              </Route>
            </Route>

            <Route path="*" element={<NotFoundPage />} />
          </Routes>
        </Suspense>
      </AuthProvider>
    </BrowserRouter>
  );
}
