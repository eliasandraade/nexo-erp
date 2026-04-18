import { BrowserRouter, Routes, Route } from "react-router-dom";
import { AuthProvider } from "@/modules/auth/context/AuthContext";
import { AuthLayout } from "@/app/layouts/AuthLayout";
import { MainAppLayout } from "@/app/layouts/MainAppLayout";
import { PosLayout } from "@/app/layouts/PosLayout";
import { PlatformLayout } from "@/app/layouts/PlatformLayout";
import { ProtectedRoute } from "./ProtectedRoute";
import { ModuleRoute } from "./ModuleRoute";
import { PlatformRoute } from "./PlatformRoute";
import PlatformDashboardPage from "@/modules/platform/pages/PlatformDashboardPage";
import PlatformTenantsPage from "@/modules/platform/pages/PlatformTenantsPage";
import PlatformTenantDetailPage from "@/modules/platform/pages/PlatformTenantDetailPage";
import PlatformSystemPage from "@/modules/platform/pages/PlatformSystemPage";
import PlatformActivityPage from "@/modules/platform/pages/PlatformActivityPage";
import PlatformTrialPage from "@/modules/platform/pages/PlatformTrialPage";
import PlatformFlagsPage from "@/modules/platform/pages/PlatformFlagsPage";
import ImpersonatePage from "@/pages/ImpersonatePage";
import LandingPage from "@/modules/landing/pages/LandingPage";

// Auth pages
import LoginPage from "@/modules/auth/pages/LoginPage";
import RegisterPage from "@/modules/auth/pages/RegisterPage";
import CheckEmailPage from "@/modules/auth/pages/CheckEmailPage";
import VerifyEmailPage from "@/modules/auth/pages/VerifyEmailPage";
import PerfilPage from "@/modules/profile/pages/PerfilPage";

// Core module pages
import DashboardPage from "@/modules/dashboard/pages/DashboardPage";
import VendasPage from "@/modules/sales/pages/VendasPage";
import VendaDetailPage from "@/modules/sales/pages/VendaDetailPage";
import ProdutosPage from "@/modules/products/pages/ProdutosPage";
import ProductFormPage from "@/modules/products/pages/ProductFormPage";
import EstoquePage from "@/modules/inventory/pages/EstoquePage";
import MovimentacoesPage from "@/modules/inventory/pages/MovimentacoesPage";
import AjustesPage from "@/modules/inventory/pages/AjustesPage";
import ClientesPage from "@/modules/customers/pages/ClientesPage";
import CustomerFormPage from "@/modules/customers/pages/CustomerFormPage";
import FornecedoresPage from "@/modules/suppliers/pages/FornecedoresPage";
import SupplierFormPage from "@/modules/suppliers/pages/SupplierFormPage";
import UsuariosPage from "@/modules/users/pages/UsuariosPage";
import UserFormPage from "@/modules/users/pages/UserFormPage";
import PermissoesPage from "@/modules/users/pages/PermissoesPage";
import CaixaPage from "@/modules/cash/pages/CaixaPage";
import AuditoriaPage from "@/modules/audit/pages/AuditoriaPage";
import ConfiguracoesPage from "@/modules/settings/pages/ConfiguracoesPage";
import NotFoundPage from "@/pages/NotFoundPage";

// Varejo module pages
import PdvPage from "@/modules/sales/pages/PdvPage";

// Restaurante module pages + layouts
import { WaiterLayout }  from "@/app/layouts/WaiterLayout";
import { KitchenLayout } from "@/app/layouts/KitchenLayout";
import FloorPage            from "@/modules/restaurante/pages/FloorPage";
import OrderPage            from "@/modules/restaurante/pages/OrderPage";
import KitchenPage          from "@/modules/restaurante/pages/KitchenPage";
import RestauranteSetupPage from "@/modules/restaurante/pages/RestauranteSetupPage";

/**
 * AuthProvider lives inside BrowserRouter so it can call useNavigate() for
 * the logout redirect. Route guards are nested in order:
 *   ProtectedRoute → verifies authentication (waits for isReady)
 *   ModuleRoute    → verifies module subscription
 */
export function AppRouter() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          {/* Public: landing page */}
          <Route path="/" element={<LandingPage />} />

          {/* Public: impersonation entry (opened in new tab by platform admin) */}
          <Route path="/impersonate" element={<ImpersonatePage />} />

          {/* Public: auth pages */}
          <Route element={<AuthLayout />}>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/register" element={<RegisterPage />} />
            <Route path="/check-email" element={<CheckEmailPage />} />
            <Route path="/verify-email" element={<VerifyEmailPage />} />
          </Route>

          {/* Protected: varejo — PDV uses its own full-screen layout */}
          <Route element={<ProtectedRoute />}>
            <Route element={<ModuleRoute moduleKey="varejo" />}>
              <Route element={<PosLayout />}>
                <Route path="/pdv" element={<PdvPage />} />
              </Route>
            </Route>
          </Route>

          {/* Restaurante — waiter pages (mobile-first, no sidebar) */}
          <Route element={<ProtectedRoute />}>
            <Route element={<ModuleRoute moduleKey="restaurante" />}>
              <Route element={<WaiterLayout />}>
                <Route path="/restaurante" element={<FloorPage />} />
                <Route path="/restaurante/mesa/:tableId" element={<OrderPage />} />
                <Route path="/restaurante/comanda/:orderId" element={<OrderPage />} />
              </Route>
              <Route element={<KitchenLayout />}>
                <Route path="/restaurante/cozinha" element={<KitchenPage />} />
              </Route>
              {/* Setup — with main sidebar */}
              <Route element={<MainAppLayout />}>
                <Route path="/restaurante/configurar" element={<RestauranteSetupPage />} />
              </Route>
            </Route>
          </Route>

          {/* Protected: core routes — no module gate */}
          <Route element={<ProtectedRoute />}>
            <Route element={<MainAppLayout />}>
              <Route path="/dashboard"              element={<DashboardPage />} />
              <Route path="/vendas"                 element={<VendasPage />} />
              <Route path="/vendas/:id"             element={<VendaDetailPage />} />
              <Route path="/produtos"               element={<ProdutosPage />} />
              <Route path="/produtos/novo"          element={<ProductFormPage />} />
              <Route path="/produtos/:id"           element={<ProductFormPage />} />
              <Route path="/estoque"                element={<EstoquePage />} />
              <Route path="/estoque/movimentacoes"  element={<MovimentacoesPage />} />
              <Route path="/estoque/ajustes"        element={<AjustesPage />} />
              <Route path="/clientes"               element={<ClientesPage />} />
              <Route path="/clientes/novo"          element={<CustomerFormPage />} />
              <Route path="/clientes/:id"           element={<CustomerFormPage />} />
              <Route path="/fornecedores"           element={<FornecedoresPage />} />
              <Route path="/fornecedores/novo"      element={<SupplierFormPage />} />
              <Route path="/fornecedores/:id"       element={<SupplierFormPage />} />
              <Route path="/usuarios"               element={<UsuariosPage />} />
              <Route path="/usuarios/novo"          element={<UserFormPage />} />
              <Route path="/usuarios/:id"           element={<UserFormPage />} />
              <Route path="/usuarios/permissoes"    element={<PermissoesPage />} />
              <Route path="/caixa"                  element={<CaixaPage />} />
              <Route path="/auditoria"              element={<AuditoriaPage />} />
              <Route path="/configuracoes"          element={<ConfiguracoesPage />} />
              <Route path="/perfil"                 element={<PerfilPage />} />
            </Route>
          </Route>

          {/* Platform admin — requires type: "platform" in session */}
          <Route element={<PlatformRoute />}>
            <Route element={<PlatformLayout />}>
              <Route path="/platform" element={<PlatformDashboardPage />} />
              <Route path="/platform/tenants" element={<PlatformTenantsPage />} />
              <Route path="/platform/tenants/:tenantId" element={<PlatformTenantDetailPage />} />
              <Route path="/platform/trial"    element={<PlatformTrialPage />} />
              <Route path="/platform/activity" element={<PlatformActivityPage />} />
              <Route path="/platform/system" element={<PlatformSystemPage />} />
              <Route path="/platform/flags"  element={<PlatformFlagsPage />} />
            </Route>
          </Route>

          <Route path="*" element={<NotFoundPage />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}
