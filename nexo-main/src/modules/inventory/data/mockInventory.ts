import type { InventoryItem, InventoryMovement, InventoryAlert } from "../types";

export const mockInventoryItems: InventoryItem[] = [
  { id: "inv-1", productId: "p-1", code: "PRD-001", description: "Camiseta Branca M", category: "Vestuário", unit: "UN", currentStock: 3, minStock: 10, status: "low", lastMovement: "2025-12-18T14:30:00" },
  { id: "inv-2", productId: "p-2", code: "PRD-002", description: "Bermuda Cargo G", category: "Vestuário", unit: "UN", currentStock: 0, minStock: 5, status: "zero", lastMovement: "2025-12-10T09:00:00" },
  { id: "inv-3", productId: "p-3", code: "PRD-003", description: "Tênis Runner Pro", category: "Calçados", unit: "UN", currentStock: 45, minStock: 8, status: "normal", lastMovement: "2025-12-20T16:45:00" },
  { id: "inv-4", productId: "p-4", code: "PRD-004", description: "Meia Esportiva", category: "Acessórios", unit: "PCT", currentStock: 5, minStock: 15, status: "low", lastMovement: "2025-12-19T11:20:00" },
  { id: "inv-5", productId: "p-5", code: "PRD-005", description: "Boné Preto", category: "Acessórios", unit: "UN", currentStock: 2, minStock: 8, status: "low", lastMovement: "2025-12-17T08:50:00" },
  { id: "inv-6", productId: "p-6", code: "PRD-006", description: "Jaqueta Corta-Vento", category: "Vestuário", unit: "UN", currentStock: 120, minStock: 10, status: "high", lastMovement: "2025-12-20T10:15:00" },
  { id: "inv-7", productId: "p-7", code: "PRD-007", description: "Calça Moletom P", category: "Vestuário", unit: "UN", currentStock: 22, minStock: 12, status: "normal", lastMovement: "2025-12-19T15:30:00" },
  { id: "inv-8", productId: "p-8", code: "PRD-008", description: "Chinelo Slide", category: "Calçados", unit: "UN", currentStock: 0, minStock: 20, status: "zero", lastMovement: "2025-12-05T12:00:00" },
  { id: "inv-9", productId: "p-9", code: "PRD-009", description: "Óculos de Sol Sport", category: "Acessórios", unit: "UN", currentStock: 35, minStock: 5, status: "normal", lastMovement: "2025-12-20T09:10:00" },
  { id: "inv-10", productId: "p-10", code: "PRD-010", description: "Garrafa Térmica 500ml", category: "Acessórios", unit: "UN", currentStock: 80, minStock: 10, status: "high", lastMovement: "2025-12-15T14:00:00" },
];

export const mockMovements: InventoryMovement[] = [
  { id: "mov-1", date: "2025-12-20T16:45:00", productId: "p-3", productDescription: "Tênis Runner Pro", type: "entry", quantity: 20, origin: "Fornecedor ABC", destination: "Loja Centro", user: "Carlos Silva", reason: "Reposição programada" },
  { id: "mov-2", date: "2025-12-20T10:15:00", productId: "p-6", productDescription: "Jaqueta Corta-Vento", type: "transfer", quantity: 30, origin: "CD Principal", destination: "Loja Centro", user: "Ana Oliveira", reason: "Transferência sazonal" },
  { id: "mov-3", date: "2025-12-19T15:30:00", productId: "p-7", productDescription: "Calça Moletom P", type: "exit", quantity: 5, origin: "Loja Centro", destination: "Cliente", user: "Pedro Santos", reason: "Venda" },
  { id: "mov-4", date: "2025-12-19T11:20:00", productId: "p-4", productDescription: "Meia Esportiva", type: "adjustment", quantity: -3, origin: "Loja Centro", destination: "—", user: "Carlos Silva", reason: "Correção de inventário" },
  { id: "mov-5", date: "2025-12-18T14:30:00", productId: "p-1", productDescription: "Camiseta Branca M", type: "exit", quantity: 7, origin: "Loja Centro", destination: "Cliente", user: "Ana Oliveira", reason: "Venda" },
  { id: "mov-6", date: "2025-12-17T08:50:00", productId: "p-5", productDescription: "Boné Preto", type: "exit", quantity: 6, origin: "Loja Centro", destination: "Cliente", user: "Pedro Santos", reason: "Venda" },
  { id: "mov-7", date: "2025-12-15T14:00:00", productId: "p-10", productDescription: "Garrafa Térmica 500ml", type: "entry", quantity: 50, origin: "Fornecedor XYZ", destination: "CD Principal", user: "Carlos Silva", reason: "Compra" },
  { id: "mov-8", date: "2025-12-10T09:00:00", productId: "p-2", productDescription: "Bermuda Cargo G", type: "exit", quantity: 5, origin: "Loja Centro", destination: "Cliente", user: "Ana Oliveira", reason: "Venda" },
];

export const mockAlerts: InventoryAlert[] = [
  { id: "alert-1", title: "Estoque zerado", description: "Bermuda Cargo G está sem estoque há 10 dias.", severity: "critical", suggestedAction: "Repor estoque", productId: "p-2" },
  { id: "alert-2", title: "Estoque zerado", description: "Chinelo Slide está sem estoque há 15 dias.", severity: "critical", suggestedAction: "Repor estoque", productId: "p-8" },
  { id: "alert-3", title: "Abaixo do mínimo", description: "Camiseta Branca M com apenas 3 unidades (mín: 10).", severity: "warning", suggestedAction: "Repor estoque", productId: "p-1" },
  { id: "alert-4", title: "Abaixo do mínimo", description: "Meia Esportiva com apenas 5 pacotes (mín: 15).", severity: "warning", suggestedAction: "Transferir entre lojas", productId: "p-4" },
  { id: "alert-5", title: "Estoque alto com baixo giro", description: "Jaqueta Corta-Vento com 120 unidades e vendas abaixo da média.", severity: "info", suggestedAction: "Aplicar campanha", productId: "p-6" },
  { id: "alert-6", title: "Estoque alto com baixo giro", description: "Garrafa Térmica 500ml com 80 unidades sem movimentação recente.", severity: "info", suggestedAction: "Revisar giro do produto", productId: "p-10" },
];

export const mockStores = ["Loja Centro", "Loja Shopping Norte", "Loja Bairro Sul", "CD Principal"];
