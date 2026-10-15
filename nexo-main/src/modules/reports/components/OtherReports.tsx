import { Link } from "react-router-dom";
import { ChartColumnBig, ChefHat, ConciergeBell, HardHat, Landmark, Users, Wallet, Warehouse, type LucideIcon } from "lucide-react";
import { SectionCard } from "@/components/shared/SectionCard";
import { useAuth } from "@/modules/auth/context/AuthContext";

interface ReportLink {
  to: string;
  title: string;
  detail: string;
  icon: LucideIcon;
  /** Shown only when the company has this module. */
  module?: string;
}

const LINKS: ReportLink[] = [
  { to: "/financeiro", title: "Financeiro", detail: "Resultado por competência ou por caixa, por categoria.", icon: Landmark },
  { to: "/financeiro/fluxo", title: "Fluxo de caixa", detail: "Entradas, saídas e saldo dia a dia, com o que vai vencer.", icon: ChartColumnBig },
  { to: "/caixa", title: "Caixa", detail: "Sessões, sangrias, suprimentos e relatório de fechamento.", icon: Wallet },
  { to: "/estoque", title: "Estoque", detail: "Saldo por produto, movimentações e alertas.", icon: Warehouse },
  { to: "/clientes", title: "Clientes", detail: "Cadastro e histórico de compras de cada cliente.", icon: Users },
  { to: "/service/financeiro", title: "Service — financeiro", detail: "Receita, comissões e resultado por profissional.", icon: ConciergeBell, module: "service" },
  { to: "/restaurante/relatorios", title: "Restaurante", detail: "Vendas do salão e do delivery, CMV e despesas.", icon: ChefHat, module: "restaurante" },
  { to: "/build", title: "Obras", detail: "Orçado × realizado e custos de cada obra.", icon: HardHat, module: "build" },
];

/** The other reports of the system, in the screens where they live — only those of the company's modules. */
export function OtherReports() {
  const { session } = useAuth();
  const modules = session?.modules ?? [];
  const links = LINKS.filter((l) => !l.module || modules.includes(l.module));

  return (
    <SectionCard title="Outros relatórios" description="Cada área tem o seu relatório na própria tela.">
      <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {links.map(({ to, title, detail, icon: Icon }) => (
          <li key={to}>
            <Link to={to} className="flex h-full items-start gap-3 rounded-md border p-3 hover:bg-muted/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
              <Icon className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
              <span>
                <span className="block text-sm font-medium">{title}</span>
                <span className="block text-xs text-muted-foreground">{detail}</span>
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </SectionCard>
  );
}
