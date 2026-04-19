import { apiClient } from "@/services/api-client";

export interface UserApiDto {
  id: string;
  tenantId: string;
  fullName: string;
  email: string;
  login: string;
  phone: string | null;
  role: string;
  status: string;
  requirePasswordChange: boolean;
  notes: string | null;
  lastAccessAt: string | null;
  passwordChangedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateUserPayload {
  fullName: string;
  email: string;
  login: string;
  password: string;
  role: string;
  phone?: string | null;
  notes?: string | null;
  requirePasswordChange?: boolean;
}

export interface UpdateUserPayload {
  fullName: string;
  email: string;
  role: string;
  phone?: string | null;
  notes?: string | null;
  status?: string | null;
}

export const listUsers = (): Promise<UserApiDto[]> =>
  apiClient.get<UserApiDto[]>("/users");

export const getUserById = (id: string): Promise<UserApiDto> =>
  apiClient.get<UserApiDto>(`/users/${id}`);

export const createUser = (payload: CreateUserPayload): Promise<UserApiDto> =>
  apiClient.post<UserApiDto>("/users", payload);

export const updateUser = (id: string, payload: UpdateUserPayload): Promise<UserApiDto> =>
  apiClient.put<UserApiDto>(`/users/${id}`, payload);

export const changePassword = (id: string, currentPassword: string, newPassword: string): Promise<void> =>
  apiClient.post<void>(`/users/${id}/change-password`, { currentPassword, newPassword });

export const adminResetPassword = (id: string, newPassword: string): Promise<void> =>
  apiClient.post<void>(`/users/${id}/admin-reset-password`, { newPassword });

export interface ValidateManagerResult {
  success: boolean;
  errorMessage: string | null;
  fullName: string | null;
  role: string | null;
}

export const validateManager = (
  login: string,
  password: string
): Promise<ValidateManagerResult> =>
  apiClient.post<ValidateManagerResult>("/users/validate-manager", { login, password });
