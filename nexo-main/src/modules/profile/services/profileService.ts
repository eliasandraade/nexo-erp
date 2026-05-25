import { userService } from "@/modules/users/services/userService";
import { apiClient } from "@/services/api-client";
import type { User } from "@/modules/users/types";

export interface ChangePasswordInput {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

/**
 * Self-service profile adapter.
 * Scoped to the currently logged-in user's own data.
 * Admin user management stays in userService / UsuariosPage.
 */
export const profileService = {
  /**
   * Fetch the full user record for the given userId.
   * Uses userService as the single source of truth for user data.
   */
  async getProfile(userId: string): Promise<User> {
    const user = await userService.getById(userId);
    if (!user) throw new Error("Perfil não encontrado.");
    return user;
  },

  /**
   * Self-service password change.
   * POST /api/users/{userId}/change-password
   * Requires the current password for verification.
   */
  async changePassword(
    userId: string,
    input: ChangePasswordInput
  ): Promise<void> {
    if (!input.currentPassword) {
      throw new Error("Informe a senha atual.");
    }
    if (!input.newPassword || input.newPassword.length < 6) {
      throw new Error("A nova senha deve ter pelo menos 6 caracteres.");
    }
    if (input.newPassword !== input.confirmPassword) {
      throw new Error("As senhas não coincidem.");
    }

    await apiClient.post<void>(`/users/${userId}/change-password`, {
      currentPassword: input.currentPassword,
      newPassword: input.newPassword,
    });
  },
};
