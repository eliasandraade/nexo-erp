import type { UserRole } from "@/modules/users/types";
import type { AuthSession } from "../types";

/**
 * Returns the home route for a given session.
 * Used after login and as the fallback redirect for role-guarded routes.
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
 * Returns true if the given role + modules combination can access the given path.
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
