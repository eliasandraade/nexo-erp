import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  fetchEmployees, createEmployee, updateEmployee,
  fetchExpenses, createExpense, updateExpense, deleteExpense,
  type CreateEmployeeRequest, type UpdateEmployeeRequest,
  type CreateExpenseRequest,
} from "../api/employees-expenses.api";

// ── Query keys ────────────────────────────────────────────────────────────────

export const EMPLOYEES_KEY = (includeInactive = false) =>
  ["restaurante", "employees", includeInactive] as const;

export const EXPENSES_KEY = (from?: string, to?: string) =>
  ["restaurante", "expenses", from, to] as const;

// ── Employee hooks ────────────────────────────────────────────────────────────

export function useEmployees(includeInactive = false) {
  return useQuery({
    queryKey: EMPLOYEES_KEY(includeInactive),
    queryFn:  () => fetchEmployees(includeInactive),
    staleTime: 60_000,
  });
}

export function useCreateEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateEmployeeRequest) => createEmployee(req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: ["restaurante", "employees"] }),
  });
}

export function useUpdateEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpdateEmployeeRequest }) =>
      updateEmployee(id, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["restaurante", "employees"] }),
  });
}

// ── Expense hooks ─────────────────────────────────────────────────────────────

export function useExpenses(from?: string, to?: string) {
  return useQuery({
    queryKey: EXPENSES_KEY(from, to),
    queryFn:  () => fetchExpenses(from, to),
    enabled:  !!from && !!to,
  });
}

export function useCreateExpense() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateExpenseRequest) => createExpense(req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: ["restaurante", "expenses"] }),
  });
}

export function useUpdateExpense() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: CreateExpenseRequest }) =>
      updateExpense(id, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["restaurante", "expenses"] }),
  });
}

export function useDeleteExpense() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteExpense(id),
    onSuccess:  () => qc.invalidateQueries({ queryKey: ["restaurante", "expenses"] }),
  });
}
