import type { AppSettings } from "../types";

export const defaultSettings: AppSettings = {
  company: {
    name: "Andrade Systems",
    tradeName: "Andrade Corp",
    cnpj: "12.345.678/0001-99",
    email: "contato@andradesystems.com.br",
    phone: "(11) 3000-1234",
  },
  operation: {
    defaultStore: "Loja Centro",
    defaultOperator: "",
  },
  inventory: {
    noMovementAlertDays: 30,
    minStockBehavior: "alert",
    enableLowStockAlerts: true,
    enableZeroStockAlerts: true,
    enableHighRotationAlerts: false,
  },
  commission: {
    defaultCommissionRate: 3,
    enableProductCommission: false,
    policyNotes: "",
  },
  pos: {
    allowValueDiscount: true,
    allowPercentDiscount: true,
    requireManagerAuth: true,
    maxDiscountPercent: 20,
  },
  system: {
    language: "pt-BR",
    dateFormat: "dd/MM/yyyy",
    currencySymbol: "R$",
  },
};
