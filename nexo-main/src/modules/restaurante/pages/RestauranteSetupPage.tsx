import { useState, useEffect } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Plus, ChevronDown, ChevronRight, Pencil, Check, X, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { toast } from "sonner";
import { listAreas, createArea, updateArea, listTables, createTable, updateTable, getFoodSettings } from "../api/restaurante.api";
import type { AreaDto, TableDto } from "../types";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useUpdateOperationalCosts } from "../hooks/useFoodSettings";

// ── Inline editable area row ──────────────────────────────────────────────────
function AreaRow({ area, tables }: { area: AreaDto; tables: TableDto[] }) {
  const qc = useQueryClient();
  const [expanded, setExpanded]     = useState(false);
  const [editing, setEditing]       = useState(false);
  const [name, setName]             = useState(area.name);
  const [addingTable, setAddingTable] = useState(false);
  const [tableNum, setTableNum]     = useState("");
  const [tableCap, setTableCap]     = useState("4");

  const updateAreaMut = useMutation({
    mutationFn: () => updateArea(area.id, { name, description: null as any, isActive: area.isActive }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["restaurante-areas-setup"] }); setEditing(false); },
  });

  const addTableMut = useMutation({
    mutationFn: () => createTable({ areaId: area.id, number: tableNum.trim(), capacity: parseInt(tableCap) || 4 }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["restaurante-tables-setup"] }); setTableNum(""); setTableCap("4"); setAddingTable(false); },
  });

  const toggleActiveMut = useMutation({
    mutationFn: () => updateArea(area.id, { name: area.name, description: null as any, isActive: !area.isActive }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["restaurante-areas-setup"] }),
  });

  const areaTables = tables.filter(t => t.areaId === area.id);

  return (
    <div className="border border-border rounded-xl overflow-hidden">
      {/* Header */}
      <div className="flex items-center gap-2 px-4 py-3 bg-card">
        <button onClick={() => setExpanded(v => !v)} className="text-muted-foreground">
          {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        </button>

        {editing ? (
          <Input
            value={name}
            onChange={e => setName(e.target.value)}
            className="h-7 text-sm flex-1"
            autoFocus
            onKeyDown={e => e.key === "Enter" && updateAreaMut.mutate()}
          />
        ) : (
          <span className="flex-1 font-medium text-sm">{area.name}</span>
        )}

        <span className="text-xs text-muted-foreground">{areaTables.length} mesa(s)</span>

        {!area.isActive && (
          <span className="text-xs bg-muted text-muted-foreground px-2 py-0.5 rounded-full">inativa</span>
        )}

        {editing ? (
          <>
            <button onClick={() => updateAreaMut.mutate()} className="text-primary"><Check className="h-4 w-4" /></button>
            <button onClick={() => { setEditing(false); setName(area.name); }} className="text-muted-foreground"><X className="h-4 w-4" /></button>
          </>
        ) : (
          <button onClick={() => setEditing(true)} className="text-muted-foreground hover:text-foreground">
            <Pencil className="h-3.5 w-3.5" />
          </button>
        )}

        <button
          onClick={() => toggleActiveMut.mutate()}
          className={`text-xs px-2 py-0.5 rounded-full border transition-colors ${
            area.isActive
              ? "border-border text-muted-foreground hover:border-destructive hover:text-destructive"
              : "border-primary text-primary hover:bg-primary/10"
          }`}
        >
          {area.isActive ? "Desativar" : "Ativar"}
        </button>
      </div>

      {/* Tables */}
      {expanded && (
        <div className="px-4 pb-3 pt-2 bg-muted/30 space-y-2">
          {areaTables.length === 0 && !addingTable && (
            <p className="text-xs text-muted-foreground py-2">Nenhuma mesa nesta área.</p>
          )}

          {areaTables.map(t => (
            <TableRowItem key={t.id} table={t} />
          ))}

          {addingTable ? (
            <div className="flex items-center gap-2 pt-1">
              <Input
                placeholder="Nº mesa (ex: 1)"
                value={tableNum}
                onChange={e => setTableNum(e.target.value)}
                className="h-8 text-sm w-28"
                autoFocus
              />
              <Input
                placeholder="Capacidade"
                type="number"
                min={1}
                value={tableCap}
                onChange={e => setTableCap(e.target.value)}
                className="h-8 text-sm w-28"
              />
              <button
                onClick={() => tableNum.trim() && addTableMut.mutate()}
                disabled={!tableNum.trim() || addTableMut.isPending}
                className="text-primary disabled:opacity-40"
              >
                <Check className="h-4 w-4" />
              </button>
              <button onClick={() => setAddingTable(false)} className="text-muted-foreground">
                <X className="h-4 w-4" />
              </button>
            </div>
          ) : (
            <button
              onClick={() => setAddingTable(true)}
              className="flex items-center gap-1.5 text-xs text-primary py-1"
            >
              <Plus className="h-3.5 w-3.5" /> Adicionar mesa
            </button>
          )}
        </div>
      )}
    </div>
  );
}

// ── Single table row (inline edit) ────────────────────────────────────────────
function TableRowItem({ table }: { table: TableDto }) {
  const qc = useQueryClient();
  const [editing, setEditing]   = useState(false);
  const [num, setNum]           = useState(table.number);
  const [cap, setCap]           = useState(String(table.capacity));

  const mut = useMutation({
    mutationFn: () => updateTable(table.id, { areaId: table.areaId, number: num, capacity: parseInt(cap) || table.capacity, isActive: table.isActive }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["restaurante-tables-setup"] }); setEditing(false); },
  });

  const toggleMut = useMutation({
    mutationFn: () => updateTable(table.id, { areaId: table.areaId, number: table.number, capacity: table.capacity, isActive: !table.isActive }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["restaurante-tables-setup"] }),
  });

  return (
    <div className="flex items-center gap-2 text-sm py-1 border-b border-border/40 last:border-0">
      {editing ? (
        <>
          <Input value={num} onChange={e => setNum(e.target.value)} className="h-7 w-24 text-xs" />
          <Input value={cap} onChange={e => setCap(e.target.value)} type="number" className="h-7 w-20 text-xs" placeholder="Cap." />
          <button onClick={() => mut.mutate()} className="text-primary"><Check className="h-3.5 w-3.5" /></button>
          <button onClick={() => { setEditing(false); setNum(table.number); setCap(String(table.capacity)); }} className="text-muted-foreground"><X className="h-3.5 w-3.5" /></button>
        </>
      ) : (
        <>
          <span className="flex-1">Mesa <strong>{table.number}</strong> <span className="text-muted-foreground text-xs">· {table.capacity} pessoas</span></span>
          {!table.isActive && <span className="text-xs text-muted-foreground">(inativa)</span>}
          <button onClick={() => setEditing(true)} className="text-muted-foreground hover:text-foreground"><Pencil className="h-3 w-3" /></button>
          <button
            onClick={() => toggleMut.mutate()}
            className="text-xs text-muted-foreground hover:text-foreground"
          >
            {table.isActive ? "Desativar" : "Ativar"}
          </button>
        </>
      )}
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────
export default function RestauranteSetupPage() {
  const { session } = useAuth();
  const storeId     = session?.storeId ?? "";
  const qc          = useQueryClient();

  const { data: areas = [], isLoading } = useQuery({
    queryKey: ["restaurante-areas-setup"],
    queryFn: () => listAreas(true),
    enabled: !!storeId,
  });

  const { data: tables = [] } = useQuery({
    queryKey: ["restaurante-tables-setup"],
    queryFn: () => listTables(true),
    enabled: !!storeId,
  });

  // ── Operational costs state
  const { data: settings } = useQuery({
    queryKey: ["food-settings", storeId],
    queryFn: getFoodSettings,
    enabled: !!storeId,
    staleTime: 60_000,
  });
  const updateCostsMut = useUpdateOperationalCosts(storeId);
  const [gasRate, setGasRate]     = useState<string>("");
  const [laborRate, setLaborRate] = useState<string>("");

  useEffect(() => {
    if (settings) {
      setGasRate(settings.costPerMinuteGas?.toString() ?? "0");
      setLaborRate(settings.costPerMinuteLaborRate?.toString() ?? "0");
    }
  }, [settings]);

  const handleSaveCosts = () => {
    updateCostsMut.mutate(
      { costPerMinuteGas: parseFloat(gasRate) || 0, costPerMinuteLaborRate: parseFloat(laborRate) || 0 },
      {
        onSuccess: () => toast.success("Custos operacionais salvos!"),
        onError:   () => toast.error("Erro ao salvar custos operacionais."),
      }
    );
  };

  const [addingArea, setAddingArea] = useState(false);
  const [newAreaName, setNewAreaName] = useState("");

  const createAreaMut = useMutation({
    mutationFn: () => createArea({ name: newAreaName.trim() }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["restaurante-areas-setup"] });
      setNewAreaName("");
      setAddingArea(false);
    },
  });

  return (
    <div className="p-6 max-w-2xl mx-auto space-y-6">
      <div>
        <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Configuração do Restaurante</h1>
        <p className="text-sm text-muted-foreground mt-0.5">Gerencie áreas e mesas do salão.</p>
      </div>

      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-medium text-foreground">Áreas e Mesas</h2>
          {!addingArea && (
            <Button size="sm" variant="outline" onClick={() => setAddingArea(true)}>
              <Plus className="h-3.5 w-3.5 mr-1" /> Nova área
            </Button>
          )}
        </div>

        {addingArea && (
          <div className="flex items-center gap-2 p-3 border border-dashed border-primary/40 rounded-xl">
            <Input
              placeholder="Nome da área (ex: Salão, Terraço)"
              value={newAreaName}
              onChange={e => setNewAreaName(e.target.value)}
              className="h-8 text-sm"
              autoFocus
              onKeyDown={e => e.key === "Enter" && newAreaName.trim() && createAreaMut.mutate()}
            />
            <button
              onClick={() => newAreaName.trim() && createAreaMut.mutate()}
              disabled={!newAreaName.trim() || createAreaMut.isPending}
              className="text-primary disabled:opacity-40"
            >
              <Check className="h-4 w-4" />
            </button>
            <button onClick={() => { setAddingArea(false); setNewAreaName(""); }} className="text-muted-foreground">
              <X className="h-4 w-4" />
            </button>
          </div>
        )}

        {isLoading ? (
          <div className="space-y-2">
            {[1, 2].map(i => <div key={i} className="h-12 rounded-xl bg-muted animate-pulse" />)}
          </div>
        ) : areas.length === 0 && !addingArea ? (
          <div className="text-center py-10 text-muted-foreground text-sm border border-dashed border-border rounded-xl">
            Nenhuma área cadastrada. Crie uma área para começar.
          </div>
        ) : (
          <div className="space-y-2">
            {areas.map(area => (
              <AreaRow key={area.id} area={area} tables={tables} />
            ))}
          </div>
        )}
      </div>

      {/* ── Custos operacionais ──────────────────────────────────────────── */}
      <div className="space-y-3">
        <div>
          <h2 className="text-sm font-medium text-foreground">Custos operacionais</h2>
          <p className="text-xs text-muted-foreground mt-0.5">
            Usado no cálculo de CMV das fichas técnicas. Informe o custo por minuto de gás e mão de obra.
          </p>
        </div>

        <div className="p-4 border border-border rounded-xl space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label htmlFor="gas-rate" className="text-sm">Custo de gás (por minuto)</Label>
              <div className="relative">
                <span className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground text-sm">R$</span>
                <Input
                  id="gas-rate"
                  type="number"
                  min={0}
                  step={0.0001}
                  value={gasRate}
                  onChange={(e) => setGasRate(e.target.value)}
                  className="pl-9 text-sm"
                  placeholder="0.0000"
                />
              </div>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="labor-rate" className="text-sm">Custo de mão de obra (por minuto)</Label>
              <div className="relative">
                <span className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground text-sm">R$</span>
                <Input
                  id="labor-rate"
                  type="number"
                  min={0}
                  step={0.0001}
                  value={laborRate}
                  onChange={(e) => setLaborRate(e.target.value)}
                  className="pl-9 text-sm"
                  placeholder="0.0000"
                />
              </div>
            </div>
          </div>
          <Button
            size="sm"
            onClick={handleSaveCosts}
            disabled={updateCostsMut.isPending}
          >
            {updateCostsMut.isPending && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
            Salvar custos
          </Button>
        </div>
      </div>
    </div>
  );
}
