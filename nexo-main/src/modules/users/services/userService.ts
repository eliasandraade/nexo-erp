import type { User, UserFormInput, PermissionMatrix, UserRole } from "../types";
import { rolePresets } from "../data/mockUsers";
import {
  listUsers, getUserById, createUser, updateUser, listAvailableStores,
  type UserApiDto,
} from "../api/users.api";

function dtoToUser(dto: UserApiDto): User {
  return {
    id:                    dto.id,
    name:                  dto.fullName,
    email:                 dto.email,
    login:                 dto.login,
    phone:                 dto.phone ?? "",
    role:                  dto.role as UserRole,
    company:               "",
    store:                 "",
    status:                dto.status as User["status"],
    lastAccess:            dto.lastAccessAt,
    lastPasswordChange:    dto.passwordChangedAt,
    requirePasswordChange: dto.requirePasswordChange,
    notes:                 dto.notes ?? "",
    createdAt:             dto.createdAt,
    createdBy:             "",
    updatedAt:             dto.updatedAt,
  };
}

export const userService = {
  async list(): Promise<User[]> {
    const dtos = await listUsers();
    return dtos.map(dtoToUser);
  },

  async getById(id: string): Promise<User | undefined> {
    try {
      return dtoToUser(await getUserById(id));
    } catch {
      return undefined;
    }
  },

  async create(input: UserFormInput): Promise<User> {
    return dtoToUser(await createUser({
      fullName:              input.name.trim(),
      email:                 input.email.trim(),
      login:                 input.login.trim(),
      password:              input.password ?? "nexo@temp",
      role:                  input.role,
      phone:                 input.phone || null,
      notes:                 input.notes || null,
      requirePasswordChange: input.requirePasswordChange,
    }));
  },

  async update(id: string, input: Partial<UserFormInput>): Promise<User> {
    const current = await this.getById(id);
    if (!current) throw new Error("Usuário não encontrado");
    return dtoToUser(await updateUser(id, {
      fullName: (input.name  ?? current.name).trim(),
      email:    (input.email ?? current.email).trim(),
      role:     input.role   ?? current.role,
      phone:    input.phone  != null ? (input.phone || null) : (current.phone || null),
      notes:    input.notes  != null ? (input.notes || null) : (current.notes || null),
      status:   input.status ?? current.status,
    }));
  },

  async listStores(): Promise<string[]> {
    const stores = await listAvailableStores();
    return stores.map((s) => s.name);
  },

  async getPermissionsByRole(role: UserRole): Promise<PermissionMatrix> {
    const preset = rolePresets.find((p) => p.role === role);
    return preset ? { ...preset.permissions } : {};
  },

  async updatePermissions(role: UserRole, permissions: PermissionMatrix): Promise<void> {
    const preset = rolePresets.find((p) => p.role === role);
    if (preset) preset.permissions = { ...permissions };
  },

  validateManagerAuthorization(
    _login: string,
    _password: string
  ): { success: true; user: User } | { success: false; error: string } {
    // Kept for backward compatibility — use validateManagerAuthorizationAsync instead.
    return { success: false, error: "Use validateManagerAuthorizationAsync." };
  },

  async validateManagerAuthorizationAsync(
    login: string,
    password: string
  ): Promise<{ success: true; user: User } | { success: false; error: string }> {
    const { validateManager } = await import("../api/users.api");
    const result = await validateManager(login, password);
    if (!result.success) {
      return { success: false, error: result.errorMessage ?? "Autorização negada." };
    }
    const user: User = {
      id: "", name: result.fullName ?? login, email: "", login,
      phone: "", role: result.role as UserRole,
      company: "", store: "", status: "active",
      lastAccess: null, lastPasswordChange: null,
      requirePasswordChange: false, notes: "",
      createdAt: "", createdBy: "", updatedAt: "",
    };
    return { success: true, user };
  },
};
