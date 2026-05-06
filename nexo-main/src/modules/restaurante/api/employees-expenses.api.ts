import { apiClient } from "@/services/api-client";

// ── Predefined expense categories ─────────────────────────────────────────────

export const EXPENSE_CATEGORIES = [
  "Energia", "Gás", "Água", "Internet",
  "Impostos", "Manutenção", "Aluguel",
  "Embalagem", "Publicidade", "Outros",
] as const;

export type ExpenseCategory = typeof EXPENSE_CATEGORIES[number];

// ── Employee types ────────────────────────────────────────────────────────────

export interface EmployeeDto {
  id:            string;
  name:          string;
  role:          string;
  admissionDate: string; // "yyyy-MM-dd"
  monthlySalary: number;
  notes:         string | null;
  isActive:      boolean;
  createdAt:     string;
}

export interface CreateEmployeeRequest {
  name:          string;
  role:          string;
  admissionDate: string; // "yyyy-MM-dd"
  monthlySalary: number;
  notes?:        string | null;
}

export interface UpdateEmployeeRequest extends CreateEmployeeRequest {
  isActive: boolean;
}

// ── Expense types ─────────────────────────────────────────────────────────────

export interface ExpenseDto {
  id:             string;
  description:    string;
  category:       string;
  amount:         number;
  competenceDate: string; // "yyyy-MM-dd"
  paymentDate:    string | null; // "yyyy-MM-dd"
  isRecurring:    boolean;
  createdAt:      string;
}

export interface CreateExpenseRequest {
  description:    string;
  category:       string;
  amount:         number;
  competenceDate: string; // "yyyy-MM-dd"
  paymentDate?:   string | null;
  isRecurring:    boolean;
}

// ── Employee fetch functions ──────────────────────────────────────────────────

export const fetchEmployees = (includeInactive = false): Promise<EmployeeDto[]> =>
  apiClient.get<EmployeeDto[]>(`/restaurante/employees?includeInactive=${includeInactive}`);

export const createEmployee = (req: CreateEmployeeRequest): Promise<EmployeeDto> =>
  apiClient.post<EmployeeDto>("/restaurante/employees", req);

export const updateEmployee = (id: string, req: UpdateEmployeeRequest): Promise<EmployeeDto> =>
  apiClient.put<EmployeeDto>(`/restaurante/employees/${id}`, req);

// ── Expense fetch functions ───────────────────────────────────────────────────

export const fetchExpenses = (from?: string, to?: string): Promise<ExpenseDto[]> => {
  const p = new URLSearchParams();
  if (from) p.set("from", from);
  if (to)   p.set("to",   to);
  const qs = p.toString();
  return apiClient.get<ExpenseDto[]>(`/restaurante/expenses${qs ? `?${qs}` : ""}`);
};

export const createExpense = (req: CreateExpenseRequest): Promise<ExpenseDto> =>
  apiClient.post<ExpenseDto>("/restaurante/expenses", req);

export const updateExpense = (id: string, req: CreateExpenseRequest): Promise<ExpenseDto> =>
  apiClient.put<ExpenseDto>(`/restaurante/expenses/${id}`, req);

export const deleteExpense = (id: string): Promise<void> =>
  apiClient.delete<void>(`/restaurante/expenses/${id}`);
