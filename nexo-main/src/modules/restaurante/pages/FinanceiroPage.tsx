import { useState, useMemo } from "react";
import {
  TrendingUp, TrendingDown, DollarSign, ShoppingBag,
  Users, Receipt, Target, Lightbulb,
  ArrowUpDown, ArrowUp, ArrowDown, Search,
  Plus, Check, X, Pencil, Trash2,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { PageHeader } from "@/components/shared/PageHeader";
import { OperationalCostsCard } from "../components/OperationalCostsCard";
import { toast } from "sonner";
import { useCmvReport, useFinanceiroSummary } from "../hooks/use-financeiro";
import {
  useEmployees, useCreateEmployee, useUpdateEmployee,
  useExpenses, useCreateExpense, useUpdateExpense, useDeleteExpense,
} from "../hooks/use-employees-expenses";
import { EXPENSE_CATEGORIES } from "../api/employees-expenses.api";
import type { CmvReportItemDto, FinanceiroSummaryDto } from "../api/financeiro.api";
import type { EmployeeDto } from "../api/employees-expenses.api";

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(v: number) {
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

function fmtPct(v: number) {
  return `${v.toFixed(1)}%`;
}

function cmvColor(pct: number): string {
  if (pct < 30) return "text-green-600 dark:text-green-400";
  if (pct <= 40) return "text-yellow-600 dark:text-yellow-400";
  return "text-red-600 dark:text-red-400";
}

function cmvBadge(pct: number): string {
  if (pct < 30) return "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400";
  if (pct <= 40) return "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-400";
  return "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400";
}

const MONTHS = [
  "Janeiro","Fevereiro","Março","Abril","Maio","Junho",
  "Julho","Agosto","Setembro","Outubro","Novembro","Dezembro",
];

function monthBounds(year: number, month: number): { from: string; to: string } {
  const pad  = (n: number) => String(n).padStart(2, "0");
  const last = new Date(year, month, 0).getDate();
  return { from: `${year}-${pad(month)}-01`, to: `${year}-${pad(month)}-${pad(last)}` };
}

function prevMonthBounds(year: number, month: number) {
  const d = new Date(year, month - 2, 1); // month-2 because Date uses 0-based months
  return monthBounds(d.getFullYear(), d.getMonth() + 1);
}

// ── KPI Card ──────────────────────────────────────────────────────────────────

interface KpiCardProps {
  icon:   React.ElementType;
  label:  string;
  value:  string;
  sub?:   string;
  color?: string;
}

function KpiCard({ icon: Icon, label, value, sub, color = "text-primary" }: KpiCardProps) {
  return (
    <div className="rounded-xl border border-border bg-card p-4 flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <div className={cn("p-2 rounded-lg bg-muted/60", color)}>
          <Icon className="h-4 w-4" />
        </div>
        <span className="text-xs font-medium text-muted-foreground">{label}</span>
      </div>
      <p className="text-2xl font-bold text-foreground tabular-nums leading-none">{value}</p>
      {sub && <p className="text-xs text-muted-foreground">{sub}</p>}
    </div>
  );
}

// ── CMV Table ─────────────────────────────────────────────────────────────────

type SortField = "productName" | "salePrice" | "unitCost" | "cmvPercent" | "margin";
type SortDir   = "asc" | "desc";

function CmvTable({ items }: { items: CmvReportItemDto[] }) {
  const [search,  setSearch]  = useState("");
  const [sortBy,  setSortBy]  = useState<SortField>("cmvPercent");
  const [sortDir, setSortDir] = useState<SortDir>("desc");

  const toggleSort = (field: SortField) => {
    if (sortBy === field) setSortDir(d => d === "asc" ? "desc" : "asc");
    else { setSortBy(field); setSortDir("desc"); }
  };

  const SortIcon = ({ field }: { field: SortField }) => {
    if (sortBy !== field) return <ArrowUpDown className="h-3 w-3 text-muted-foreground" />;
    return sortDir === "asc" ? <ArrowUp className="h-3 w-3" /> : <ArrowDown className="h-3 w-3" />;
  };

  const filtered = useMemo(() => {
    const q = search.toLowerCase();
    return items.filter(i =>
      i.productName.toLowerCase().includes(q) || i.productCode.toLowerCase().includes(q));
  }, [items, search]);

  const sorted = useMemo(() => [...filtered].sort((a, b) => {
    const dir = sortDir === "asc" ? 1 : -1;
    if (sortBy === "productName") return dir * a.productName.localeCompare(b.productName);
    return dir * (a[sortBy] - b[sortBy]);
  }), [filtered, sortBy, sortDir]);

  const thClass = "text-left text-xs font-medium text-muted-foreground uppercase tracking-wide px-3 py-2";
  const tdClass = "px-3 py-3 text-sm";

  return (
    <div className="space-y-3">
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input className="pl-9 text-sm" placeholder="Filtrar por nome ou código…"
          value={search} onChange={e => setSearch(e.target.value)} />
      </div>
      {sorted.length === 0 ? (
        <div className="text-center py-12 text-sm text-muted-foreground">
          Nenhum prato encontrado. Crie fichas técnicas para seus produtos do cardápio.
        </div>
      ) : (
        <div className="rounded-xl border border-border overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px]">
              <thead className="bg-muted/40 border-b border-border">
                <tr>
                  <th className={thClass}>
                    <button className="flex items-center gap-1 hover:text-foreground transition-colors"
                      onClick={() => toggleSort("productName")}>
                      Prato <SortIcon field="productName" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("salePrice")}>
                      Preço venda <SortIcon field="salePrice" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("unitCost")}>
                      Custo unitário <SortIcon field="unitCost" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("margin")}>
                      Margem <SortIcon field="margin" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("cmvPercent")}>
                      CMV% <SortIcon field="cmvPercent" />
                    </button>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {sorted.map(item => (
                  <tr key={item.productId} className="hover:bg-muted/20 transition-colors">
                    <td className={tdClass}>
                      <p className="font-medium">{item.productName}</p>
                      <p className="text-xs text-muted-foreground">{item.productCode}</p>
                    </td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>{fmt(item.salePrice)}</td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>
                      <span>{fmt(item.unitCost)}</span>
                      {(item.gasCost > 0 || item.laborCost > 0) && (
                        <p className="text-xs text-muted-foreground">ing: {fmt(item.unitIngredientCost)}</p>
                      )}
                    </td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>
                      <p>{fmt(item.margin)}</p>
                      <p className="text-xs text-muted-foreground">{fmtPct(item.marginPercent)}</p>
                    </td>
                    <td className={cn(tdClass, "text-right")}>
                      <span className={cn(
                        "inline-block px-2 py-0.5 rounded-full text-xs font-semibold tabular-nums",
                        cmvBadge(item.cmvPercent))}>
                        {fmtPct(item.cmvPercent)}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      <p className="text-xs text-muted-foreground">
        {sorted.length} de {items.length} prato{items.length !== 1 ? "s" : ""} ·
        CMV verde &lt;30% · amarelo 30–40% · vermelho &gt;40%
      </p>
    </div>
  );
}

// ── Employees Section ─────────────────────────────────────────────────────────

function EmployeesSection() {
  const { data: employees = [], isLoading } = useEmployees(true);
  const createMut  = useCreateEmployee();
  const updateMut  = useUpdateEmployee();

  const [adding, setAdding]         = useState(false);
  const [newName, setNewName]       = useState("");
  const [newRole, setNewRole]       = useState("");
  const [newSalary, setNewSalary]   = useState("");
  const [editId, setEditId]         = useState<string | null>(null);
  const [editName, setEditName]     = useState("");
  const [editRole, setEditRole]     = useState("");
  const [editSalary, setEditSalary] = useState("");

  const today = new Date().toISOString().split("T")[0];

  const handleAdd = () => {
    if (!newName.trim() || !newSalary) return;
    createMut.mutate(
      { name: newName.trim(), role: newRole.trim(), admissionDate: today,
        monthlySalary: parseFloat(newSalary) || 0, notes: null },
      { onSuccess: () => { setAdding(false); setNewName(""); setNewRole(""); setNewSalary(""); toast.success("Funcionário adicionado!"); },
        onError:   () => toast.error("Erro ao adicionar funcionário.") });
  };

  const startEdit = (e: EmployeeDto) => {
    setEditId(e.id); setEditName(e.name); setEditRole(e.role);
    setEditSalary(String(e.monthlySalary));
  };

  const handleSaveEdit = (emp: EmployeeDto) => {
    updateMut.mutate(
      { id: emp.id, req: { name: editName.trim(), role: editRole.trim(),
          admissionDate: emp.admissionDate, monthlySalary: parseFloat(editSalary) || 0,
          notes: emp.notes, isActive: emp.isActive } },
      { onSuccess: () => { setEditId(null); toast.success("Funcionário atualizado!"); },
        onError:   () => toast.error("Erro ao atualizar.") });
  };

  const toggleActive = (emp: EmployeeDto) => {
    updateMut.mutate(
      { id: emp.id, req: { name: emp.name, role: emp.role,
          admissionDate: emp.admissionDate, monthlySalary: emp.monthlySalary,
          notes: emp.notes, isActive: !emp.isActive } },
      { onError: () => toast.error("Erro ao atualizar funcionário.") });
  };

  const activeTotal = employees.filter(e => e.isActive).reduce((s, e) => s + e.monthlySalary, 0);

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-sm font-semibold">Funcionários</h2>
          <p className="text-xs text-muted-foreground mt-0.5">
            Custo mensal total (ativos): <strong>{fmt(activeTotal)}</strong>
          </p>
        </div>
        {!adding && (
          <Button size="sm" variant="outline" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Adicionar
          </Button>
        )}
      </div>

      {adding && (
        <div className="flex items-end gap-2 p-3 border border-dashed border-primary/40 rounded-xl flex-wrap">
          <div className="space-y-1 flex-1 min-w-[140px]">
            <Label className="text-xs">Nome</Label>
            <Input value={newName} onChange={e => setNewName(e.target.value)}
              placeholder="Nome completo" className="h-8 text-sm" autoFocus />
          </div>
          <div className="space-y-1 flex-1 min-w-[120px]">
            <Label className="text-xs">Função</Label>
            <Input value={newRole} onChange={e => setNewRole(e.target.value)}
              placeholder="Cozinheiro, Garçom…" className="h-8 text-sm" />
          </div>
          <div className="space-y-1 w-32">
            <Label className="text-xs">Salário/mês</Label>
            <div className="relative">
              <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
              <Input value={newSalary} onChange={e => setNewSalary(e.target.value)}
                type="number" min={0} step={0.01} className="h-8 text-sm pl-7" placeholder="0,00" />
            </div>
          </div>
          <div className="flex gap-1">
            <button onClick={handleAdd} disabled={!newName.trim() || createMut.isPending}
              className="text-primary disabled:opacity-40">
              <Check className="h-4 w-4" />
            </button>
            <button onClick={() => { setAdding(false); setNewName(""); setNewRole(""); setNewSalary(""); }}
              className="text-muted-foreground">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>
      )}

      {isLoading ? (
        <div className="space-y-2">
          {[1, 2].map(i => <div key={i} className="h-10 rounded-lg bg-muted animate-pulse" />)}
        </div>
      ) : employees.length === 0 ? (
        <p className="text-sm text-muted-foreground py-4 text-center">
          Nenhum funcionário cadastrado.
        </p>
      ) : (
        <div className="rounded-xl border border-border overflow-hidden divide-y divide-border">
          {employees.map(emp => (
            <div key={emp.id} className={cn("flex items-center gap-2 px-4 py-3 text-sm",
              !emp.isActive && "opacity-50")}>
              {editId === emp.id ? (
                <>
                  <Input value={editName} onChange={e => setEditName(e.target.value)}
                    className="h-7 text-xs flex-1" />
                  <Input value={editRole} onChange={e => setEditRole(e.target.value)}
                    className="h-7 text-xs w-28" placeholder="Função" />
                  <div className="relative w-28">
                    <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
                    <Input value={editSalary} onChange={e => setEditSalary(e.target.value)}
                      type="number" className="h-7 text-xs pl-7" />
                  </div>
                  <button onClick={() => handleSaveEdit(emp)} className="text-primary">
                    <Check className="h-3.5 w-3.5" />
                  </button>
                  <button onClick={() => setEditId(null)} className="text-muted-foreground">
                    <X className="h-3.5 w-3.5" />
                  </button>
                </>
              ) : (
                <>
                  <div className="flex-1">
                    <p className="font-medium">{emp.name}</p>
                    <p className="text-xs text-muted-foreground">{emp.role}</p>
                  </div>
                  <span className="tabular-nums text-sm">{fmt(emp.monthlySalary)}<span className="text-xs text-muted-foreground">/mês</span></span>
                  <button onClick={() => startEdit(emp)} className="text-muted-foreground hover:text-foreground">
                    <Pencil className="h-3 w-3" />
                  </button>
                  <button onClick={() => toggleActive(emp)}
                    className={cn("text-xs px-2 py-0.5 rounded-full border transition-colors",
                      emp.isActive
                        ? "border-border text-muted-foreground hover:border-destructive hover:text-destructive"
                        : "border-primary text-primary hover:bg-primary/10")}>
                    {emp.isActive ? "Desativar" : "Ativar"}
                  </button>
                </>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Expenses Section ──────────────────────────────────────────────────────────

function ExpensesSection({ from, to }: { from: string; to: string }) {
  const { data: expenses = [], isLoading } = useExpenses(from, to);
  const createMut = useCreateExpense();
  const updateMut = useUpdateExpense();
  const deleteMut = useDeleteExpense();

  const [adding, setAdding]               = useState(false);
  const [desc, setDesc]                   = useState("");
  const [cat, setCat]                     = useState<string>(EXPENSE_CATEGORIES[0]);
  const [amount, setAmount]               = useState("");
  const [isRecurring, setIsRecurring]     = useState(false);

  const [editId, setEditId]               = useState<string | null>(null);
  const [editDesc, setEditDesc]           = useState("");
  const [editCat, setEditCat]             = useState<string>(EXPENSE_CATEGORIES[0]);
  const [editAmount, setEditAmount]       = useState("");
  const [editRecurring, setEditRecurring] = useState(false);

  const handleAdd = () => {
    if (!desc.trim() || !amount) return;
    createMut.mutate(
      { description: desc.trim(), category: cat, amount: parseFloat(amount) || 0,
        competenceDate: from, paymentDate: null, isRecurring },
      { onSuccess: () => { setAdding(false); setDesc(""); setAmount(""); setIsRecurring(false); toast.success("Despesa adicionada!"); },
        onError:   () => toast.error("Erro ao adicionar despesa.") });
  };

  const startEdit = (exp: { id: string; description: string; category: string; amount: number; isRecurring: boolean; competenceDate: string; paymentDate: string | null }) => {
    setEditId(exp.id);
    setEditDesc(exp.description);
    setEditCat(exp.category);
    setEditAmount(String(exp.amount));
    setEditRecurring(exp.isRecurring);
  };

  const handleSaveEdit = (exp: { id: string; competenceDate: string; paymentDate: string | null }) => {
    if (!editDesc.trim() || !editAmount) return;
    updateMut.mutate(
      { id: exp.id, req: { description: editDesc.trim(), category: editCat,
          amount: parseFloat(editAmount) || 0, competenceDate: exp.competenceDate,
          paymentDate: exp.paymentDate, isRecurring: editRecurring } },
      { onSuccess: () => { setEditId(null); toast.success("Despesa atualizada!"); },
        onError:   () => toast.error("Erro ao atualizar despesa.") });
  };

  const handleDelete = (id: string) => {
    deleteMut.mutate(id, {
      onSuccess: () => toast.success("Despesa removida."),
      onError:   () => toast.error("Erro ao remover despesa."),
    });
  };

  const total = expenses.reduce((s, e) => s + e.amount, 0);

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-sm font-semibold">Despesas Gerais</h2>
          <p className="text-xs text-muted-foreground mt-0.5">
            Total do período: <strong>{fmt(total)}</strong>
          </p>
        </div>
        {!adding && (
          <Button size="sm" variant="outline" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Adicionar
          </Button>
        )}
      </div>

      {adding && (
        <div className="flex items-end gap-2 p-3 border border-dashed border-primary/40 rounded-xl flex-wrap">
          <div className="space-y-1 flex-1 min-w-[160px]">
            <Label className="text-xs">Descrição</Label>
            <Input value={desc} onChange={e => setDesc(e.target.value)}
              placeholder="Ex: Conta de energia" className="h-8 text-sm" autoFocus />
          </div>
          <div className="space-y-1 w-36">
            <Label className="text-xs">Categoria</Label>
            <Select value={cat} onValueChange={v => setCat(v)}>
              <SelectTrigger className="h-8 text-sm">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {EXPENSE_CATEGORIES.map(c => (
                  <SelectItem key={c} value={c}>{c}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1 w-28">
            <Label className="text-xs">Valor</Label>
            <div className="relative">
              <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
              <Input value={amount} onChange={e => setAmount(e.target.value)}
                type="number" min={0} step={0.01} className="h-8 text-sm pl-7" placeholder="0,00" />
            </div>
          </div>
          <div className="flex items-center gap-1.5 pb-1">
            <input type="checkbox" id="recurring" checked={isRecurring}
              onChange={e => setIsRecurring(e.target.checked)} className="h-4 w-4" />
            <Label htmlFor="recurring" className="text-xs cursor-pointer">Recorrente</Label>
          </div>
          <div className="flex gap-1">
            <button onClick={handleAdd} disabled={!desc.trim() || !amount || createMut.isPending}
              className="text-primary disabled:opacity-40">
              <Check className="h-4 w-4" />
            </button>
            <button onClick={() => { setAdding(false); setDesc(""); setAmount(""); setIsRecurring(false); }}
              className="text-muted-foreground">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>
      )}

      {isLoading ? (
        <div className="space-y-2">
          {[1, 2].map(i => <div key={i} className="h-10 rounded-lg bg-muted animate-pulse" />)}
        </div>
      ) : expenses.length === 0 ? (
        <p className="text-sm text-muted-foreground py-4 text-center">
          Nenhuma despesa neste período.
        </p>
      ) : (
        <div className="rounded-xl border border-border overflow-hidden divide-y divide-border">
          {expenses.map(exp => (
            <div key={exp.id} className="flex items-center gap-2 px-4 py-3 text-sm">
              {editId === exp.id ? (
                <>
                  <Input value={editDesc} onChange={e => setEditDesc(e.target.value)}
                    className="h-7 text-xs flex-1" />
                  <Select value={editCat} onValueChange={v => setEditCat(v)}>
                    <SelectTrigger className="h-7 text-xs w-32">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {EXPENSE_CATEGORIES.map(c => (
                        <SelectItem key={c} value={c}>{c}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <div className="relative w-28">
                    <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
                    <Input value={editAmount} onChange={e => setEditAmount(e.target.value)}
                      type="number" className="h-7 text-xs pl-7" />
                  </div>
                  <div className="flex items-center gap-1">
                    <input type="checkbox" checked={editRecurring}
                      onChange={e => setEditRecurring(e.target.checked)} className="h-3.5 w-3.5" />
                    <span className="text-xs text-muted-foreground">Rec.</span>
                  </div>
                  <button onClick={() => handleSaveEdit(exp)} disabled={updateMut.isPending}
                    className="text-primary disabled:opacity-40">
                    <Check className="h-3.5 w-3.5" />
                  </button>
                  <button onClick={() => setEditId(null)} className="text-muted-foreground">
                    <X className="h-3.5 w-3.5" />
                  </button>
                </>
              ) : (
                <>
                  <div className="flex-1">
                    <p className="font-medium">{exp.description}</p>
                    <p className="text-xs text-muted-foreground">
                      {exp.category}{exp.isRecurring ? " · recorrente" : ""}
                    </p>
                  </div>
                  <span className="tabular-nums font-medium">{fmt(exp.amount)}</span>
                  <button onClick={() => startEdit(exp)}
                    className="text-muted-foreground hover:text-foreground transition-colors">
                    <Pencil className="h-3 w-3" />
                  </button>
                  <button onClick={() => handleDelete(exp.id)}
                    className="text-muted-foreground hover:text-destructive transition-colors">
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Insight Cards ─────────────────────────────────────────────────────────────

interface InsightCardsProps {
  cmvItems:    CmvReportItemDto[];
  summary:     FinanceiroSummaryDto | undefined;
  prevSummary: FinanceiroSummaryDto | undefined;
  employees:   EmployeeDto[];
}

function InsightCards({ cmvItems, summary, prevSummary, employees }: InsightCardsProps) {
  const highCmvCount = cmvItems.filter(i => i.cmvPercent > 35).length;

  const topEmployee = employees
    .filter(e => e.isActive)
    .sort((a, b) => b.monthlySalary - a.monthlySalary)[0];

  const prevProfit  = prevSummary?.operationalProfit ?? 0;
  const currProfit  = summary?.operationalProfit     ?? 0;
  const profitDelta = prevSummary ? currProfit - prevProfit : null;

  return (
    <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
      {/* Insight 1: High CMV dishes */}
      <div className="rounded-xl border border-border bg-card p-4 space-y-1">
        <div className="flex items-center gap-2">
          <Lightbulb className="h-4 w-4 text-yellow-500" />
          <span className="text-xs font-medium text-muted-foreground">CMV elevado (&gt;35%)</span>
        </div>
        {highCmvCount === 0 ? (
          <p className="text-sm font-semibold text-green-600 dark:text-green-400">
            Nenhum prato acima de 35% 🎉
          </p>
        ) : (
          <p className="text-sm font-semibold text-red-600 dark:text-red-400">
            {highCmvCount} prato{highCmvCount !== 1 ? "s" : ""} acima de 35%
          </p>
        )}
        <p className="text-xs text-muted-foreground">
          {cmvItems.length} fichas técnicas no total.
        </p>
      </div>

      {/* Insight 2: Lucro vs mês anterior */}
      <div className="rounded-xl border border-border bg-card p-4 space-y-1">
        <div className="flex items-center gap-2">
          <TrendingUp className="h-4 w-4 text-blue-500" />
          <span className="text-xs font-medium text-muted-foreground">Lucro vs mês anterior</span>
        </div>
        {profitDelta === null ? (
          <p className="text-sm text-muted-foreground">Carregando…</p>
        ) : (
          <p className={cn("text-sm font-semibold tabular-nums",
            profitDelta >= 0 ? "text-green-600 dark:text-green-400" : "text-red-600 dark:text-red-400")}>
            {profitDelta >= 0 ? "+" : ""}{fmt(profitDelta)}
          </p>
        )}
        <p className="text-xs text-muted-foreground">
          Lucro atual: {fmt(currProfit)}
        </p>
      </div>

      {/* Insight 3: Top employee cost */}
      <div className="rounded-xl border border-border bg-card p-4 space-y-1">
        <div className="flex items-center gap-2">
          <Users className="h-4 w-4 text-purple-500" />
          <span className="text-xs font-medium text-muted-foreground">Maior custo — pessoal</span>
        </div>
        {!topEmployee ? (
          <p className="text-sm text-muted-foreground">Nenhum funcionário ativo.</p>
        ) : (
          <>
            <p className="text-sm font-semibold">{topEmployee.name}</p>
            <p className="text-xs text-muted-foreground">
              {fmt(topEmployee.monthlySalary)}/mês · {topEmployee.role}
            </p>
          </>
        )}
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export default function FinanceiroPage() {
  const now = new Date();
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [year,  setYear]  = useState(now.getFullYear());

  const { from, to }                   = monthBounds(year, month);
  const { from: prevFrom, to: prevTo } = prevMonthBounds(year, month);

  const { data: cmvData,    isLoading: cmvLoading } = useCmvReport();
  const { data: summary,    isLoading: sumLoading } = useFinanceiroSummary(from, to);
  const { data: prevSummary                       } = useFinanceiroSummary(prevFrom, prevTo);
  const { data: employees = []                    } = useEmployees(true);

  const isLoading = cmvLoading || sumLoading;

  return (
    <div className="p-6 space-y-8">
      <PageHeader
        eyebrow="Orken Menu"
        title="Financeiro"
        description="CMV por prato, pessoal, despesas e KPIs do período selecionado."
      />

      {/* ── Period picker ─────────────────────────────────────────────────── */}
      <div className="flex items-center gap-3 flex-wrap">
        <Select value={String(month)} onValueChange={v => setMonth(Number(v))}>
          <SelectTrigger className="w-36 text-sm">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {MONTHS.map((m, i) => (
              <SelectItem key={i + 1} value={String(i + 1)}>{m}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="icon" className="h-9 w-9 text-sm"
            onClick={() => setYear(y => y - 1)}>‹</Button>
          <span className="tabular-nums text-sm font-medium w-12 text-center">{year}</span>
          <Button variant="outline" size="icon" className="h-9 w-9 text-sm"
            onClick={() => setYear(y => y + 1)} disabled={year >= now.getFullYear()}>›</Button>
        </div>
        <span className="text-xs text-muted-foreground">{from} → {to}</span>
      </div>

      {/* ── KPI Cards — row 1: revenue ────────────────────────────────────── */}
      <div className="space-y-3">
        <h2 className="text-sm font-semibold text-foreground">Resultado do período</h2>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <KpiCard icon={DollarSign} label="Faturamento bruto"
            value={isLoading ? "—" : fmt(summary?.revenue ?? 0)}
            sub={isLoading ? undefined : `${summary?.ordersCount ?? 0} comanda(s)`}
            color="text-blue-600" />
          <KpiCard icon={ShoppingBag} label="Custo de mercadoria (CMG)"
            value={isLoading ? "—" : fmt(summary?.totalCostOfGoodsSold ?? 0)}
            sub="CMG do período" color="text-orange-600" />
          <KpiCard icon={TrendingUp} label="CMV% ponderado"
            value={isLoading ? "—" : fmtPct(summary?.weightedCmvPercent ?? 0)}
            sub="Baseado nos pedidos"
            color={isLoading ? "text-muted-foreground" : cmvColor(summary?.weightedCmvPercent ?? 0)} />
          <KpiCard icon={TrendingDown} label="Margem bruta"
            value={isLoading ? "—" : fmt(summary?.grossMargin ?? 0)}
            sub={isLoading || !summary?.revenue ? undefined
              : fmtPct(100 - (summary.totalCostOfGoodsSold / summary.revenue) * 100)}
            color="text-green-600" />
        </div>

        {/* ── KPI Cards — row 2: costs and profit ──────────────────────── */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <KpiCard icon={Users} label="Custo de pessoal"
            value={isLoading ? "—" : fmt(summary?.totalPersonnelCost ?? 0)}
            sub="Salários mensais (ativos)" color="text-purple-600" />
          <KpiCard icon={Receipt} label="Despesas do período"
            value={isLoading ? "—" : fmt(summary?.totalFixedExpenses ?? 0)}
            sub="Energia, água, etc." color="text-rose-600" />
          <KpiCard icon={TrendingUp} label="Lucro operacional"
            value={isLoading ? "—" : fmt(summary?.operationalProfit ?? 0)}
            sub="Margem − pessoal − despesas"
            color={isLoading ? "text-muted-foreground"
              : (summary?.operationalProfit ?? 0) >= 0
                ? "text-green-600" : "text-red-600"} />
          <KpiCard icon={Target} label="Ponto de equilíbrio"
            value={isLoading ? "—" : summary?.breakEvenRevenue
              ? fmt(summary.breakEvenRevenue) : "N/D"}
            sub="Faturamento mínimo necessário" color="text-indigo-600" />
        </div>
      </div>

      {/* ── Insights ──────────────────────────────────────────────────────── */}
      <div className="space-y-2">
        <h2 className="text-sm font-semibold">Insights</h2>
        <InsightCards
          cmvItems={cmvData?.items ?? []}
          summary={summary}
          prevSummary={prevSummary}
          employees={employees}
        />
      </div>

      {/* ── CMV Table ─────────────────────────────────────────────────────── */}
      <div className="space-y-2">
        <div>
          <h2 className="text-sm font-semibold">CMV por prato</h2>
          <p className="text-xs text-muted-foreground mt-0.5">
            Custo atual baseado nas fichas técnicas. Ordene por coluna clicando no cabeçalho.
          </p>
        </div>
        {cmvLoading ? (
          <div className="space-y-2">
            {[1, 2, 3].map(i => <div key={i} className="h-14 rounded-xl bg-muted animate-pulse" />)}
          </div>
        ) : (
          <CmvTable items={cmvData?.items ?? []} />
        )}
      </div>

      {/* ── Parâmetros de custo (CMV) — alimentam o CMV acima ──────────────── */}
      <OperationalCostsCard />

      {/* ── Funcionários ──────────────────────────────────────────────────── */}
      <EmployeesSection />

      {/* ── Despesas ──────────────────────────────────────────────────────── */}
      <ExpensesSection from={from} to={to} />
    </div>
  );
}
