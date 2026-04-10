import type { User, PermissionMatrix, RolePreset } from "../types";

export const mockStores = ["Loja Centro", "Loja Norte", "Loja Sul", "Loja Oeste"];

/**
 * Mock credential store for manager authorization.
 * In production this would be replaced by a real auth API call.
 * Only Gerente and Diretoria users have entries here.
 */
export const mockManagerPasswords: Record<string, string> = {
  "carlos.andrade": "diretor123",
  "fernanda.lima": "gerente123",
};

export const mockUsers: User[] = [
  {
    id: "usr-1",
    name: "Carlos Andrade",
    email: "carlos@andradesystems.com",
    login: "carlos.andrade",
    phone: "(11) 99000-1234",
    role: "diretoria",
    company: "Andrade Systems",
    store: "—",
    status: "active",
    lastAccess: "2026-03-12T08:30:00",
    lastPasswordChange: "2026-02-10T14:00:00",
    requirePasswordChange: false,
    notes: "",
    createdAt: "2024-01-15T10:00:00",
    createdBy: "Sistema",
    updatedAt: "2026-03-10T09:00:00",
  },
  {
    id: "usr-2",
    name: "Fernanda Lima",
    email: "fernanda@nexo.com",
    login: "fernanda.lima",
    phone: "(11) 98500-5678",
    role: "gerente",
    company: "Andrade Systems",
    store: "Loja Centro",
    status: "active",
    lastAccess: "2026-03-11T17:45:00",
    lastPasswordChange: "2026-01-20T10:00:00",
    requirePasswordChange: false,
    notes: "Responsável pela operação da Loja Centro.",
    createdAt: "2024-03-01T09:00:00",
    createdBy: "Carlos Andrade",
    updatedAt: "2026-03-05T11:00:00",
  },
  {
    id: "usr-3",
    name: "Rafael Souza",
    email: "rafael@nexo.com",
    login: "rafael.souza",
    phone: "(11) 97200-9012",
    role: "vendedor",
    company: "Andrade Systems",
    store: "Loja Centro",
    status: "active",
    lastAccess: "2026-03-12T07:00:00",
    lastPasswordChange: "2025-12-15T09:00:00",
    requirePasswordChange: false,
    notes: "",
    createdAt: "2024-06-10T08:00:00",
    createdBy: "Fernanda Lima",
    updatedAt: "2025-12-15T09:00:00",
  },
  {
    id: "usr-4",
    name: "Juliana Costa",
    email: "juliana@nexo.com",
    login: "juliana.costa",
    phone: "(11) 96800-3456",
    role: "estoquista",
    company: "Andrade Systems",
    store: "Loja Norte",
    status: "active",
    lastAccess: "2026-03-11T16:00:00",
    lastPasswordChange: "2026-03-01T08:00:00",
    requirePasswordChange: false,
    notes: "",
    createdAt: "2025-01-05T10:00:00",
    createdBy: "Carlos Andrade",
    updatedAt: "2026-03-01T08:00:00",
  },
  {
    id: "usr-5",
    name: "Pedro Mendes",
    email: "pedro@nexo.com",
    login: "pedro.mendes",
    phone: "(11) 95500-7890",
    role: "vendedor",
    company: "Andrade Systems",
    store: "Loja Sul",
    status: "inactive",
    lastAccess: "2025-11-20T14:00:00",
    lastPasswordChange: "2025-08-10T10:00:00",
    requirePasswordChange: true,
    notes: "Afastado temporariamente.",
    createdAt: "2024-09-15T09:00:00",
    createdBy: "Fernanda Lima",
    updatedAt: "2025-11-25T09:00:00",
  },
  {
    id: "usr-6",
    name: "Mariana Oliveira",
    email: "mariana@nexo.com",
    login: "mariana.oliveira",
    phone: "(11) 94400-2345",
    role: "gerente",
    company: "Andrade Systems",
    store: "Loja Norte",
    status: "blocked",
    lastAccess: "2026-01-05T10:00:00",
    lastPasswordChange: "2025-06-01T10:00:00",
    requirePasswordChange: false,
    notes: "Bloqueada por tentativas excessivas de login.",
    createdAt: "2024-05-20T08:00:00",
    createdBy: "Carlos Andrade",
    updatedAt: "2026-01-10T09:00:00",
  },
];

const allActions = (mod: string) => {
  const map: Record<string, string[]> = {
    dashboard: ["view", "export"],
    pdv: ["view", "create", "cancel"],
    orcamentos: ["view", "create", "edit", "delete", "approve", "cancel", "export"],
    produtos: ["view", "create", "edit", "delete", "export"],
    estoque: ["view", "adjust", "transfer", "export"],
    clientes: ["view", "create", "edit", "delete", "export"],
    fornecedores: ["view", "create", "edit", "delete", "export"],
    usuarios: ["view", "create", "edit", "delete"],
    comissoes: ["view", "edit", "approve", "export"],
    caixa: ["view", "create", "cancel", "export"],
    relatorios: ["view", "export"],
    insights: ["view"],
    auditoria: ["view", "export"],
    configuracoes: ["view", "edit"],
  };
  return map[mod] ?? [];
};

const diretoriaPerms: PermissionMatrix = {};
const gerentePerms: PermissionMatrix = {};
const vendedorPerms: PermissionMatrix = {};
const estoquistaPerms: PermissionMatrix = {};

const modules = [
  "dashboard","pdv","orcamentos","produtos","estoque","clientes","fornecedores",
  "usuarios","comissoes","caixa","relatorios","insights","auditoria","configuracoes",
];

modules.forEach((m) => {
  diretoriaPerms[m] = allActions(m) as any;
  // Gerente: most except usuarios/configuracoes write
  gerentePerms[m] = m === "usuarios" ? ["view"] : m === "configuracoes" ? ["view"] : m === "auditoria" ? ["view"] : allActions(m) as any;
  // Vendedor: PDV, orcamentos, clientes read, estoque view
  vendedorPerms[m] =
    m === "pdv" ? ["view", "create", "cancel"] :
    m === "orcamentos" ? ["view", "create", "edit"] :
    m === "clientes" ? ["view", "create", "edit"] :
    m === "estoque" ? ["view"] :
    m === "produtos" ? ["view"] :
    m === "dashboard" ? ["view"] :
    m === "comissoes" ? ["view"] :
    [];
  // Estoquista: estoque, produtos, dashboard
  estoquistaPerms[m] =
    m === "estoque" ? ["view", "adjust", "transfer"] :
    m === "produtos" ? ["view", "create", "edit"] :
    m === "dashboard" ? ["view"] :
    m === "fornecedores" ? ["view"] :
    [];
});

export const rolePresets: RolePreset[] = [
  { role: "diretoria", label: "Diretoria", permissions: diretoriaPerms },
  { role: "gerente", label: "Gerente", permissions: gerentePerms },
  { role: "vendedor", label: "Vendedor", permissions: vendedorPerms },
  { role: "estoquista", label: "Estoquista", permissions: estoquistaPerms },
];
