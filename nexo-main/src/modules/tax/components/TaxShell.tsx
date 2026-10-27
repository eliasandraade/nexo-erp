import { Link, NavLink } from "react-router-dom";
import { Info, UserCog } from "lucide-react";
import { cn } from "@/lib/utils";

const TABS = [
  { to: "/tributos", label: "Visão geral", end: true },
  { to: "/tributos/perfil", label: "Perfil" },
  { to: "/tributos/calculos", label: "Cálculos" },
  { to: "/tributos/simulacoes", label: "Simulações" },
  { to: "/tributos/oportunidades", label: "Oportunidades" },
  { to: "/tributos/creditos", label: "Créditos e split" },
  { to: "/tributos/regras", label: "Regras e fontes" },
];

/** Links between the tax pages, scrollable on small screens (the page itself never scrolls sideways). */
export function TaxSubNav() {
  return (
    <nav aria-label="Inteligência tributária" className="-mx-1 overflow-x-auto">
      <ul className="flex min-w-max gap-1 border-b border-border px-1">
        {TABS.map((t) => (
          <li key={t.to}>
            <NavLink
              to={t.to}
              end={t.end}
              className={({ isActive }) => cn(
                "inline-block border-b-2 px-3 py-2 text-sm font-medium transition-colors",
                isActive ? "border-foreground text-foreground" : "border-transparent text-muted-foreground hover:text-foreground",
              )}
            >
              {t.label}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
}

/** A quiet note: information, not alarm. */
export function TaxNote({ children, className }: Readonly<{ children: React.ReactNode; className?: string }>) {
  return (
    <p className={cn("flex items-start gap-2 rounded-md border border-dashed p-3 text-sm text-muted-foreground", className)}>
      <Info className="mt-0.5 h-4 w-4 shrink-0" />
      <span>{children}</span>
    </p>
  );
}

/** Shown when the server refuses because there is no tax profile yet. */
export function ProfileMissingNotice() {
  return (
    <div role="alert" className="flex flex-col gap-2 rounded-md border border-warning/40 bg-warning/5 p-3 text-sm sm:flex-row sm:items-center sm:justify-between">
      <span className="flex items-start gap-2">
        <UserCog className="mt-0.5 h-4 w-4 shrink-0 text-warning" />
        Sem perfil tributário não há cálculo: o regime, a UF e o município decidem quais regras valem.
      </span>
      <Link to="/tributos/perfil" className="font-medium text-primary underline-offset-4 hover:underline">Cadastrar perfil tributário</Link>
    </div>
  );
}

export function Field({ id, label, hint, className, children }: Readonly<{ id: string; label: string; hint?: string; className?: string; children: React.ReactNode }>) {
  return (
    <div className={cn("min-w-0 space-y-1", className)}>
      <label htmlFor={id} className="text-sm font-medium leading-none">{label}</label>
      {children}
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}

export function ProblemList({ problems }: Readonly<{ problems: string[] }>) {
  if (problems.length === 0) return null;
  return (
    <ul className="list-disc space-y-0.5 pl-5 text-sm text-destructive">
      {problems.map((p) => <li key={p}>{p}</li>)}
    </ul>
  );
}
