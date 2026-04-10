import type { User, UserFormInput, PermissionMatrix, UserRole } from "../types";
import { mockUsers, mockStores, rolePresets, mockManagerPasswords } from "../data/mockUsers";
import { auditService } from "@/modules/audit/services/auditService";

const users = [...mockUsers];
const delay = (ms = 400) => new Promise((r) => setTimeout(r, ms));

export const userService = {
  async list(): Promise<User[]> {
    await delay();
    return [...users];
  },

  async getById(id: string): Promise<User | undefined> {
    await delay();
    return users.find((u) => u.id === id);
  },

  async create(input: UserFormInput): Promise<User> {
    await delay(600);
    const user: User = {
      id: `usr-${Date.now()}`,
      name: input.name,
      email: input.email,
      login: input.login,
      phone: input.phone,
      role: input.role,
      company: input.company || "Andrade Systems",
      store: input.store,
      status: input.status,
      lastAccess: null,
      lastPasswordChange: null,
      requirePasswordChange: input.requirePasswordChange,
      notes: input.notes,
      createdAt: new Date().toISOString(),
      createdBy: "Usuário atual",
      updatedAt: new Date().toISOString(),
    };
    users.unshift(user);
    auditService.addAuditRecord({
      actionType: "user_created",
      severity: "info",
      actor: "Usuário atual",
      entityType: "user",
      entityId: user.id,
      description: `Usuário "${user.name}" (${user.login}) criado com função: ${user.role}.`,
      metadata: { role: user.role, status: user.status },
    });
    return user;
  },

  async update(id: string, input: Partial<UserFormInput>): Promise<User> {
    await delay(600);
    const idx = users.findIndex((u) => u.id === id);
    if (idx === -1) throw new Error("Usuário não encontrado");
    const current = users[idx];
    const updated: User = {
      ...current,
      ...input,
      updatedAt: new Date().toISOString(),
    } as User;
    users[idx] = updated;
    auditService.addAuditRecord({
      actionType: "user_updated",
      severity: "info",
      actor: "Usuário atual",
      entityType: "user",
      entityId: id,
      description: `Usuário "${updated.name}" (${updated.login}) atualizado.`,
    });
    return updated;
  },

  async listStores(): Promise<string[]> {
    await delay(100);
    return [...mockStores];
  },

  async getPermissionsByRole(role: UserRole): Promise<PermissionMatrix> {
    await delay(200);
    const preset = rolePresets.find((p) => p.role === role);
    return preset ? { ...preset.permissions } : {};
  },

  async updatePermissions(role: UserRole, permissions: PermissionMatrix): Promise<void> {
    await delay(400);
    const preset = rolePresets.find((p) => p.role === role);
    if (preset) {
      preset.permissions = { ...permissions };
    }
  },

  /**
   * Validates manager-level credentials for authorizing sensitive operations
   * (e.g., sale cancellation). Synchronous — no async delay — so it can be
   * called inline during transaction flows.
   *
   * Returns { success: true, user } on valid Gerente/Diretoria credentials,
   * or { success: false, error } with a user-facing message on failure.
   */
  validateManagerAuthorization(
    login: string,
    password: string
  ): { success: true; user: User } | { success: false; error: string } {
    const user = users.find((u) => u.login === login);
    if (!user) return { success: false, error: "Usuário não encontrado." };
    if (user.status !== "active")
      return { success: false, error: "Usuário inativo ou bloqueado." };
    if (user.role !== "gerente" && user.role !== "diretoria")
      return { success: false, error: "Usuário não possui autorização gerencial." };
    const storedPassword = mockManagerPasswords[login];
    if (!storedPassword || password !== storedPassword) {
      auditService.addAuditRecord({
        actionType: "manager_authorization",
        severity: "critical",
        actor: login,
        entityType: "user",
        entityId: user.id,
        description: `Tentativa de autorização gerencial negada para "${login}": senha incorreta.`,
      });
      return { success: false, error: "Senha incorreta." };
    }
    auditService.addAuditRecord({
      actionType: "manager_authorization",
      severity: "warning",
      actor: login,
      entityType: "user",
      entityId: user.id,
      description: `Autorização gerencial concedida para "${user.name}" (${user.role}).`,
    });
    return { success: true, user };
  },
};
