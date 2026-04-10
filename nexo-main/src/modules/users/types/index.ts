export type UserStatus = "active" | "inactive" | "blocked";
export type UserRole = "diretoria" | "gerente" | "vendedor" | "estoquista";

export interface User {
  id: string;
  name: string;
  email: string;
  login: string;
  phone: string;
  role: UserRole;
  company: string;
  store: string;
  status: UserStatus;
  lastAccess: string | null;
  lastPasswordChange: string | null;
  requirePasswordChange: boolean;
  notes: string;
  createdAt: string;
  createdBy: string;
  updatedAt: string;
}

export interface UserFormInput {
  name: string;
  email: string;
  login: string;
  phone: string;
  role: UserRole;
  company: string;
  store: string;
  status: UserStatus;
  password?: string;
  passwordConfirm?: string;
  requirePasswordChange: boolean;
  blocked: boolean;
  notes: string;
}

export type PermissionAction =
  | "view"
  | "create"
  | "edit"
  | "delete"
  | "approve"
  | "cancel"
  | "adjust"
  | "transfer"
  | "export";

export interface PermissionModule {
  key: string;
  label: string;
  actions: PermissionAction[];
}

export type PermissionMatrix = Record<string, PermissionAction[]>;

export interface RolePreset {
  role: UserRole;
  label: string;
  permissions: PermissionMatrix;
}

export const roleLabels: Record<UserRole, string> = {
  diretoria: "Diretoria",
  gerente: "Gerente",
  vendedor: "Vendedor",
  estoquista: "Estoquista",
};

export const statusLabels: Record<UserStatus, string> = {
  active: "Ativo",
  inactive: "Inativo",
  blocked: "Bloqueado",
};

export const statusVariant: Record<UserStatus, "success" | "neutral" | "danger"> = {
  active: "success",
  inactive: "neutral",
  blocked: "danger",
};

export const permissionModules: PermissionModule[] = [
  { key: "dashboard", label: "Dashboard", actions: ["view", "export"] },
  { key: "pdv", label: "PDV", actions: ["view", "create", "cancel"] },
  { key: "orcamentos", label: "Orçamentos", actions: ["view", "create", "edit", "delete", "approve", "cancel", "export"] },
  { key: "produtos", label: "Produtos", actions: ["view", "create", "edit", "delete", "export"] },
  { key: "estoque", label: "Estoque", actions: ["view", "adjust", "transfer", "export"] },
  { key: "clientes", label: "Clientes", actions: ["view", "create", "edit", "delete", "export"] },
  { key: "fornecedores", label: "Fornecedores", actions: ["view", "create", "edit", "delete", "export"] },
  { key: "usuarios", label: "Usuários", actions: ["view", "create", "edit", "delete"] },
  { key: "comissoes", label: "Comissões", actions: ["view", "edit", "approve", "export"] },
  { key: "caixa", label: "Caixa", actions: ["view", "create", "cancel", "export"] },
  { key: "relatorios", label: "Relatórios", actions: ["view", "export"] },
  { key: "insights", label: "Insights", actions: ["view"] },
  { key: "auditoria", label: "Auditoria", actions: ["view", "export"] },
  { key: "configuracoes", label: "Configurações", actions: ["view", "edit"] },
];

export const actionLabels: Record<PermissionAction, string> = {
  view: "Visualizar",
  create: "Criar",
  edit: "Editar",
  delete: "Excluir",
  approve: "Aprovar",
  cancel: "Cancelar",
  adjust: "Ajustar",
  transfer: "Transferir",
  export: "Exportar",
};
