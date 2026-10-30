// Root namespaces owned by existing ORKEN experiences must never resolve as company slugs.
const reserved = new Set([
  "agendar",
  "rastrear",
  "menu",
  "impersonate",
  "login",
  "register",
  "check-email",
  "verify-email",
  "esqueci-senha",
  "forgot-password",
  "redefinir-senha",
  "pdv",
  "build",
  "service",
  "restaurante",
  "dashboard",
  "vendas",
  "clientes",
  "fornecedores",
  "caixa",
  "financeiro",
  "relatorios",
  "fiscal",
  "tributos",
  "listas-de-preco",
  "configuracoes",
  "assinatura",
  "usuarios",
  "auditoria",
  "estoque",
  "produtos",
  "compras",
  "workspaces",
  "perfil",
  "platform",
  "reset-password",
  "api",
  "assets",
  "acesso-encerrado",
  "app",
  "admin",
  "static",
  "www",
  "orken",
  "portal",
  "obra",
  "obras",
  "health",
  "public",
  "logout",
]);
export function resolveBuildPortal(pathname: string): boolean {
  const parts = pathname.split("/").filter(Boolean);
  return (
    parts.length === 2 &&
    parts.every(
      (part) =>
        part.length >= 3 &&
        part.length <= 60 &&
        /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(part) &&
        !reserved.has(part),
    ) &&
    !reserved.has(parts[0])
  );
}
export const BUILD_PORTAL_ROUTE = "/:companySlug/:projectSlug";
