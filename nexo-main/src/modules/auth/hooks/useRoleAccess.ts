import type { UserRole } from "@/modules/users/types";
import type { AuthSession } from "../types";

/** Where the read-only notices point. The page itself is for Diretoria and Gerente only. */
export const READ_ONLY_SUBSCRIPTION_URL = "/assinatura?motivo=somente-leitura";

/** Opens for every role, so it can be the home of a role whose module the company never had. */
const HOME_WITHOUT_MODULE = "/perfil";

/**
 * The modules whose screens open: those with access and those that expired, which stay read-only. What each one may write
 * is the server's decision: a write on an expired module answers 402.
 */
export function readableModules(session: Pick<AuthSession, "modules" | "readOnlyModules">): string[] {
  return [...session.modules, ...(session.readOnlyModules ?? [])];
}

/**
 * Returns the home route for a given session.
 * Used after login and as the fallback redirect for role-guarded routes, so it never points to a route this same role would
 * be sent back from: that is a blank screen or a redirect loop.
 */
export function homeRoute(session: AuthSession): string {
  const modules = readableModules(session);
  switch (session.role) {
    case "cozinha":
      return modules.includes("restaurante") ? "/restaurante/cozinha" : HOME_WITHOUT_MODULE;
    case "vendedor":
      if (modules.includes("varejo"))      return "/pdv";
      if (modules.includes("restaurante")) return "/restaurante";
      return HOME_WITHOUT_MODULE;
    case "estoquista":
      return "/estoque";
    case "gerente":
    case "diretoria":
    default:
      return "/dashboard";
  }
}

/**
 * Returns true if the given role + modules combination can access the given path.
 * `modules` is {@link readableModules}: an expired module still opens, read-only.
 *
 * Rules:
 * - diretoria: all paths
 * - gerente: all paths (store scoping is enforced by the backend/JWT)
 * - vendedor + varejo module: /pdv only
 * - vendedor + restaurante module: floor/orders/delivery (not management pages)
 * - estoquista: /estoque and /produtos only
 * - cozinha: /restaurante/cozinha only
 *
 * /perfil is always accessible to any authenticated role.
 */
export function canAccessPath(
  role: UserRole,
  modules: string[],
  path: string
): boolean {
  // Profile always accessible
  if (path === "/perfil") return true;

  if (role === "diretoria" || role === "gerente") return true;

  if (role === "cozinha") {
    return path === "/restaurante/cozinha";
  }

  if (role === "estoquista") {
    return path.startsWith("/estoque") || path.startsWith("/produtos");
  }

  if (role === "vendedor") {
    const hasVarejo      = modules.includes("varejo");
    const hasRestaurante = modules.includes("restaurante");

    if (hasVarejo && path.startsWith("/pdv")) return true;

    if (hasRestaurante) {
      // Operational routes only — no management pages
      if (path === "/restaurante")                        return true;
      if (path.startsWith("/restaurante/mesa"))           return true;
      if (path.startsWith("/restaurante/comanda"))        return true;
      if (path.startsWith("/restaurante/delivery"))       return true;
      if (path.startsWith("/restaurante/cozinha"))        return true;
    }

    return false;
  }

  return false;
}
