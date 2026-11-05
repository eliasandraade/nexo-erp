import { Link } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { canAccessPath, READ_ONLY_SUBSCRIPTION_URL } from "@/modules/auth/hooks/useRoleAccess";
import { WORKSPACES } from "@/modules/workspace/config";
import { cn } from "@/lib/utils";

const namesList = (names: string[]) =>
  names.length < 2 ? names.join("") : `${names.slice(0, -1).join(", ")} e ${names[names.length - 1]}`;

/**
 * The permanent line of a company in read-only, at the top of every layout: no module with access, or a module that expired.
 * Names the module when another one still works, because there the rest of the system writes normally. Promises no purchase:
 * see docs/engineering/tenant-access-enforcement.md.
 */
export function ReadOnlyNotice({ className }: Readonly<{ className?: string }>) {
  const { session } = useAuth();
  if (session?.type !== "tenant") return null;

  const wholeCompany = session.modules.length === 0;
  const expired = WORKSPACES.filter((w) => session.readOnlyModules?.includes(w.moduleKey)).map((w) => w.name);
  if (!wholeCompany && expired.length === 0) return null;

  return (
    <div role="status" className={cn("flex shrink-0 flex-wrap items-center justify-center gap-x-2 bg-muted px-4 py-1 text-xs text-muted-foreground", className)}>
      <span>
        {wholeCompany
          ? "Somente leitura: sua empresa está sem assinatura ativa. Dá para consultar e exportar, mas não registrar nem alterar."
          : `Somente leitura em ${namesList(expired)}: dá para consultar e exportar, mas não registrar nem alterar. O restante segue normal.`}
      </span>
      {canAccessPath(session.role, [], "/assinatura") && (
        <Link to={READ_ONLY_SUBSCRIPTION_URL} className="font-medium underline">Ver assinatura</Link>
      )}
    </div>
  );
}
