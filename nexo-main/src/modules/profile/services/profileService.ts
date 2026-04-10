import { userService } from "@/modules/users/services/userService";
import type { User } from "@/modules/users/types";

export interface ChangePasswordInput {
  newPassword: string;
  confirmPassword: string;
}

const delay = (ms = 600) => new Promise((r) => setTimeout(r, ms));

/**
 * Self-service profile adapter.
 * Scoped to the currently logged-in user's own data.
 * Admin user management stays in userService / UsuariosPage.
 *
 * Future backend: replace with apiClient.get/post calls to
 * /profile and /auth/change-password endpoints.
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
   * Simulate a self-service password change.
   * Validates match and minimum length; logs simulated success.
   * In production: POST /auth/change-password { newPassword }
   */
  async changePassword(
    _userId: string,
    input: ChangePasswordInput
  ): Promise<void> {
    await delay();

    if (!input.newPassword || input.newPassword.length < 6) {
      throw new Error("A nova senha deve ter pelo menos 6 caracteres.");
    }

    if (input.newPassword !== input.confirmPassword) {
      throw new Error("As senhas não coincidem.");
    }

    // Mock success — production would update the credential server-side
  },
};
