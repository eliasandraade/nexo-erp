import { lazy, Suspense } from "react";
import { BrowserRouter, Routes, Route, Navigate, useParams } from "react-router-dom";
import { AuthProvider } from "@/modules/auth/context/AuthContext";
import { WorkspaceProvider } from "@/modules/workspace/WorkspaceContext";
import { AppErrorBoundary } from "@/components/shared/AppErrorBoundary";
import { AuthLayout } from "@/app/layouts/AuthLayout";
import { MainAppLayout } from "@/app/layouts/MainAppLayout";
import { PosLayout } from "@/app/layouts/PosLayout";
import { PlatformLayout } from "@/app/layouts/PlatformLayout";
import { ImpersonationBanner } from "@/components/ImpersonationBanner";
import { ProtectedRoute } from "./ProtectedRoute";
import { ModuleRoute } from "./ModuleRoute";
import { PlatformRoute } from "./PlatformRoute";
import { RoleRoute } from "./RoleRoute";
import { ServiceModuleRoute } from "@/modules/service/routing/ServiceModuleRoute";
import { ServicePresetProvider } from "@/modules/service/context/ServicePresetContext";

// ── Static imports: auth pages only — absolute critical path ─────────────────
// Login/Register must render without a spinner (auth redirect lands here).
import LoginPage      from "@/modules/auth/pages/LoginPage";
import RegisterPage   from "@/modules/auth/pages/RegisterPage";
import CheckEmailPage from "@/modules/auth/pages/CheckEmailPage";
import VerifyEmailPage from "@/modules/auth/pages/VerifyEmailPage";
import ForgotPasswordPage from "@/modules/auth/pages/ForgotPasswordPage";
import ResetPasswordPage from "@/modules/auth/pages/ResetPasswordPage";
import NotFoundPage   from "@/pages/NotFoundPage";

// ── Lazy imports: everything else ────────────────────────────────────────────
// Public pages that aren't on the auth critical path (landing, portal) +
// all authenticated app pages. Each group becomes its own chunk.

// Public — lazy because app users never visit these
const LandingPage        = lazy(() => import("@/modules/landing/pages/LandingPage"));
const PortalMenuPage     = lazy(() => import("@/modules/portal/pages/PortalMenuPage"));
const PortalTrackingPage = lazy(() => import("@/modules/portal/pages/PortalTrackingPage"));
const ServiceBookingPage = lazy(() => import("@/modules/service-portal/pages/BookingPage"));
const ServiceManageBookingPage = lazy(() => import("@/modules/service-portal/pages/ManageBookingPage"));

const ImpersonatePage       = lazy(() => import("@/pages/ImpersonatePage"));
const PerfilPage            = lazy(() => import("@/modules/profile/pages/PerfilPage"));
const ModuleSelectionPage   = lazy(() => import("@/modules/workspace/pages/ModuleSelectionPage"));

// Core / dashboard
const DashboardPage         = lazy(() => import("@/modules/dashboard/pages/DashboardPage"));
const VendasPage            = lazy(() => import("@/modules/sales/pages/VendasPage"));
const VendaDetailPage       = lazy(() => import("@/modules/sales/pages/VendaDetailPage"));
const ProdutosPage          = lazy(() => import("@/modules/products/pages/ProdutosPage"));
const ProductFormPage       = lazy(() => import("@/modules/products/pages/ProductFormPage"));
const EstoquePage           = lazy(() => import("@/modules/inventory/pages/EstoquePage"));
const MovimentacoesPage     = lazy(() => import("@/modules/inventory/pages/MovimentacoesPage"));
const AjustesPage           = lazy(() => import("@/modules/inventory/pages/AjustesPage"));
const InventarioPage        = lazy(() => import("@/modules/inventory/pages/InventarioPage"));
const TransferenciasPage    = lazy(() => import("@/modules/inventory/pages/TransferenciasPage"));
const ComprasPage           = lazy(() => import("@/modules/purchases/pages/ComprasPage"));
const CompraDetailPage      = lazy(() => import("@/modules/purchases/pages/CompraDetailPage"));
const ListasDePrecoPage     = lazy(() => import("@/modules/pricelists/pages/ListasDePrecoPage"));
const LivroFinanceiroPage   = lazy(() => import("@/modules/financial/pages/FinanceiroPage"));
const ContasReceberPage     = lazy(() => import("@/modules/financial/pages/ContasReceberPage"));
const ContasPagarPage       = lazy(() => import("@/modules/financial/pages/ContasPagarPage"));
const FluxoCaixaPage        = lazy(() => import("@/modules/financial/pages/FluxoCaixaPage"));
const CoreRelatoriosPage    = lazy(() => import("@/modules/reports/pages/RelatoriosPage"));
const FiscalPage            = lazy(() => import("@/modules/fiscal/pages/FiscalPage"));
const TaxOverviewPage       = lazy(() => import("@/modules/tax/pages/TaxOverviewPage"));
const TaxProfilePage        = lazy(() => import("@/modules/tax/pages/TaxProfilePage"));
const TaxCalculationsPage   = lazy(() => import("@/modules/tax/pages/TaxCalculationsPage"));
const TaxCalculationDetailPage = lazy(() => import("@/modules/tax/pages/TaxCalculationDetailPage"));
const TaxSimulationsPage    = lazy(() => import("@/modules/tax/pages/TaxSimulationsPage"));
const TaxOpportunitiesPage  = lazy(() => import("@/modules/tax/pages/TaxOpportunitiesPage"));
const TaxOpportunityDetailPage = lazy(() => import("@/modules/tax/pages/TaxOpportunityDetailPage"));
const TaxCreditsPage        = lazy(() => import("@/modules/tax/pages/TaxCreditsPage"));
const TaxRulesPage          = lazy(() => import("@/modules/tax/pages/TaxRulesPage"));
const ClientesPage          = lazy(() => import("@/modules/customers/pages/ClientesPage"));
const CustomerFormPage      = lazy(() => import("@/modules/customers/pages/CustomerFormPage"));
const FornecedoresPage      = lazy(() => import("@/modules/suppliers/pages/FornecedoresPage"));
const SupplierFormPage      = lazy(() => import("@/modules/suppliers/pages/SupplierFormPage"));
const UsuariosPage          = lazy(() => import("@/modules/users/pages/UsuariosPage"));
const UserFormPage          = lazy(() => import("@/modules/users/pages/UserFormPage"));
const CaixaPage             = lazy(() => import("@/modules/cash/pages/CaixaPage"));
const AuditoriaPage         = lazy(() => import("@/modules/audit/pages/AuditoriaPage"));
const ConfiguracoesPage     = lazy(() => import("@/modules/settings/pages/ConfiguracoesPage"));
const AssinaturaPage        = lazy(() => import("@/modules/billing/pages/AssinaturaPage"));

// PDV (varejo)
const PdvPage               = lazy(() => import("@/modules/sales/pages/PdvPage"));

// Build (obras)
const BuildProjectsPage     = lazy(() => import("@/modules/build/pages/BuildProjectsPage"));
const BuildProjectDetailPage = lazy(() => import("@/modules/build/pages/BuildProjectDetailPage"));

// Service (serviços) — engine shell + cadastros; remaining surfaces land in the stacked PRs
const ServiceOverviewPage   = lazy(() => import("@/modules/service/pages/ServiceOverviewPage"));
const ProfissionaisPage     = lazy(() => import("@/modules/service/pages/ProfissionaisPage"));
const CatalogoPage          = lazy(() => import("@/modules/service/pages/CatalogoPage"));
const SubjectsPage          = lazy(() => import("@/modules/service/pages/SubjectsPage"));
const AgendaPage            = lazy(() => import("@/modules/service/pages/AgendaPage"));
const OrdensPage            = lazy(() => import("@/modules/service/pages/OrdensPage"));
const OrderDetailPage       = lazy(() => import("@/modules/service/pages/OrderDetailPage"));
const PacotesPage           = lazy(() => import("@/modules/service/pages/PacotesPage"));
const CustomerPackagesPage  = lazy(() => import("@/modules/service/pages/CustomerPackagesPage"));
const CustomerPackageDetailPage = lazy(() => import("@/modules/service/pages/CustomerPackageDetailPage"));
const ServiceFinancePage = lazy(() => import("@/modules/service/pages/FinanceiroPage"));
const PagamentosPage        = lazy(() => import("@/modules/service/pages/PagamentosPage"));
const ServicePortalPage     = lazy(() => import("@/modules/service/pages/ServicePortalPage"));

// Restaurante
import { WaiterLayout }     from "@/app/layouts/WaiterLayout";
import { KitchenLayout }    from "@/app/layouts/KitchenLayout";
const FloorPage             = lazy(() => import("@/modules/restaurante/pages/FloorPage"));
const OrderPage             = lazy(() => import("@/modules/restaurante/pages/OrderPage"));
const AdicionaisPage        = lazy(() => import("@/modules/restaurante/pages/AdicionaisPage"));
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

/**
 * Redirects legacy /menu/:slug links to /:slug. The slug is reduced to a single path segment (no slash or
 * backslash), so a crafted link such as /menu/%5Cexample.com can never become a redirect to another origin.
 */
function MenuSlugRedirect() {
  const { slug } = useParams<{ slug: string }>();
  return <Navigate to={legacyMenuTarget(slug)} replace />;
}

// eslint-disable-next-line react-refresh/only-export-components -- pure helper, tested on its own
export function legacyMenuTarget(slug: string | undefined): string {
  return `/${(slug ?? "").replace(/[\\/]/g, "")}`;
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
        <ImpersonationBanner />
        <WorkspaceProvider>
        <AppErrorBoundary>
        <Suspense fallback={<PageLoader />}>
          <Routes>
            {/* Public: landing page */}
            <Route path="/" element={<LandingPage />} />

            {/* Public: Orken Service booking portal — distinct prefix (restaurant owns root /:slug;
                React Router ranks the static "agendar" segment above the dynamic :slug). */}
            <Route path="/agendar/:slug"   element={<ServiceBookingPage />} />
            <Route path="/agendar/:slug/reserva/:token" element={<ServiceManageBookingPage />} />

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
              <Route path="/esqueci-senha"   element={<ForgotPasswordPage />} />
              <Route path="/forgot-password" element={<ForgotPasswordPage />} />
              <Route path="/redefinir-senha" element={<ResetPasswordPage />} />
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

            {/* ── Service (Serviços) — management only; family gate + preset-driven shell ── */}
            <Route element={<ProtectedRoute />}>
              <Route element={<ServiceModuleRoute />}>
                <Route element={<RoleRoute path="/service" />}>
                  <Route element={<ServicePresetProvider />}>
                    <Route element={<MainAppLayout />}>
                      <Route path="/service" element={<ServiceOverviewPage />} />
                      <Route path="/service/agenda" element={<AgendaPage />} />
                      <Route path="/service/ordens" element={<OrdensPage />} />
                      <Route path="/service/ordens/:id" element={<OrderDetailPage />} />
                      <Route path="/service/pacotes" element={<PacotesPage />} />
                      <Route path="/service/customer-packages" element={<CustomerPackagesPage />} />
                      <Route path="/service/customer-packages/:id" element={<CustomerPackageDetailPage />} />
                      <Route path="/service/pagamentos" element={<PagamentosPage />} />
                      <Route path="/service/financeiro" element={<ServiceFinancePage />} />
                      <Route path="/service/profissionais" element={<ProfissionaisPage />} />
                      <Route path="/service/catalogo" element={<CatalogoPage />} />
                      <Route path="/service/subjects" element={<SubjectsPage />} />
                      <Route path="/service/portal" element={<ServicePortalPage />} />
                    </Route>
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
                    <Route path="/restaurante/adicionais" element={<AdicionaisPage />} />
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
                  <Route path="/financeiro"    element={<LivroFinanceiroPage />} />
                  <Route path="/financeiro/receber" element={<ContasReceberPage />} />
                  <Route path="/financeiro/pagar"   element={<ContasPagarPage />} />
                  <Route path="/financeiro/fluxo"   element={<FluxoCaixaPage />} />
                  <Route path="/relatorios"         element={<CoreRelatoriosPage />} />
                  <Route path="/fiscal"             element={<FiscalPage />} />
                  <Route path="/tributos"                element={<TaxOverviewPage />} />
                  <Route path="/tributos/perfil"         element={<TaxProfilePage />} />
                  <Route path="/tributos/calculos"       element={<TaxCalculationsPage />} />
                  <Route path="/tributos/calculos/:id"   element={<TaxCalculationDetailPage />} />
                  <Route path="/tributos/simulacoes"     element={<TaxSimulationsPage />} />
                  <Route path="/tributos/oportunidades"  element={<TaxOpportunitiesPage />} />
                  <Route path="/tributos/oportunidades/:id" element={<TaxOpportunityDetailPage />} />
                  <Route path="/tributos/creditos"       element={<TaxCreditsPage />} />
                  <Route path="/tributos/regras"         element={<TaxRulesPage />} />
                  <Route path="/listas-de-preco" element={<ListasDePrecoPage />} />
                  <Route path="/configuracoes" element={<ConfiguracoesPage />} />
                  <Route path="/assinatura"   element={<AssinaturaPage />} />
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
                  <Route path="/estoque/inventario"    element={<InventarioPage />} />
                  <Route path="/estoque/transferencias" element={<TransferenciasPage />} />
                  <Route path="/compras"               element={<ComprasPage />} />
                  <Route path="/compras/:id"           element={<CompraDetailPage />} />
                </Route>
              </Route>
            </Route>

            {/* ── Seleção de área de trabalho — full screen, sem sidebar ─────── */}
            <Route element={<ProtectedRoute />}>
              <Route path="/workspaces" element={<ModuleSelectionPage />} />
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
        </AppErrorBoundary>
        </WorkspaceProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}
