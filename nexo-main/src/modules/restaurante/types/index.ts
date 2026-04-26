// ── Settings ──────────────────────────────────────────────────────────────────
export interface FoodServiceSettingsDto {
  id: string;
  storeType: "restaurant" | "bar" | "pub";
  couvertEnabled: boolean;
  couvertPricePerPerson: number | null;
  couvertAutomatic: boolean;
  serviceFeeEnabled: boolean;
  serviceFeePercent: number | null;
  orderTypesEnabled: string;
}

export interface UpdateFoodServiceSettingsRequest {
  storeType: string;
  couvertEnabled: boolean;
  couvertPricePerPerson: number | null;
  couvertAutomatic: boolean;
  serviceFeeEnabled: boolean;
  serviceFeePercent: number | null;
  orderTypesEnabled: string;
}

// ── Areas ─────────────────────────────────────────────────────────────────────
export interface AreaDto {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  tableCount: number;
}

// ── Tables ────────────────────────────────────────────────────────────────────
export type TableStatus = "Available" | "Occupied" | "Reserved" | "Maintenance";

export interface TableDto {
  id: string;
  areaId: string;
  areaName: string;
  number: string;
  capacity: number;
  status: TableStatus;
  isActive: boolean;
}

// ── Orders ────────────────────────────────────────────────────────────────────
export type OrderType = "DineIn" | "Counter" | "Takeaway" | "Delivery";
export type OrderStatus =
  | "Open"
  | "InPreparation"
  | "Ready"
  | "Closed"
  | "Paid"
  | "Cancelled";
export type OrderItemStatus =
  | "Pending"
  | "Preparing"
  | "Ready"
  | "Delivered"
  | "Cancelled";

export interface OrderItemModifierDto {
  modifierId: string;
  labelSnapshot: string;
  priceSnapshot: number;
}

export interface OrderItemDto {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  total: number;
  status: OrderItemStatus;
  notes: string | null;
  modifiers: OrderItemModifierDto[];
  sentToKitchenAt: string | null;
  preparedAt: string | null;
  deliveredAt: string | null;
  cancelledAt: string | null;
}

export interface OrderDto {
  id: string;
  orderNumber: number;
  status: OrderStatus;
  orderType: OrderType;
  tableId: string | null;
  tableNumber: string | null;
  partySize: number | null;
  waiterId: string;
  customerId: string | null;
  saleId: string | null;
  itemsSubtotal: number;
  couvertAmount: number;
  serviceFeeAmount: number;
  total: number;
  notes: string | null;
  openedAt: string;
  closedAt: string | null;
  cancelledAt: string | null;
  items: OrderItemDto[];
}

// ── Order requests ────────────────────────────────────────────────────────────
export interface OpenOrderRequest {
  orderType: OrderType;
  tableId?: string | null;
  partySize?: number | null;
  customerId?: string | null;
  notes?: string | null;
}

export interface ApplyModifierRequest {
  modifierId: string;
}

export interface AddOrderItemRequest {
  productId: string;
  quantity: number;
  notes?: string | null;
  modifiers?: ApplyModifierRequest[];
}

export interface PaymentInputDto {
  method: string;
  type: string;
  amount: number;
  dueDate?: string | null;
}

export interface PayOrderRequest {
  payments: PaymentInputDto[];
  partySize?: number | null;
}

// ── Modifiers ─────────────────────────────────────────────────────────────────
export interface ModifierDto {
  id: string;
  name: string;
  priceAdjustment: number;
  sortOrder: number;
  isActive: boolean;
}

export interface ModifierGroupDto {
  id: string;
  productId: string;
  name: string;
  isRequired: boolean;
  minSelections: number;
  maxSelections: number;
  sortOrder: number;
  isActive: boolean;
  modifiers: ModifierDto[];
}

// ── Delivery Orders ───────────────────────────────────────────────────────────

export type DeliveryChannel =
  | "Portal" | "PhoneCall" | "InPerson" | "WhatsApp"
  | "IFood" | "Rappi" | "Anotaai" | "Other";

export type DeliveryOrderType = "Delivery" | "Takeaway";

export type DeliveryOrderStatus =
  | "Received" | "Accepted" | "InPreparation"
  | "ReadyForPickup" | "OutForDelivery"
  | "Delivered" | "Rejected" | "Cancelled";

export interface DeliveryItemModifierDto {
  modifierId: string | null;
  label: string;
  price: number;
}

export interface DeliveryItemDto {
  id: string;
  productId: string | null;
  productName: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
  notes: string | null;
  modifiers: DeliveryItemModifierDto[];
}

export interface DeliveryOrderDto {
  id: string;
  orderNumber: number;
  trackingToken: string;
  channel: DeliveryChannel;
  orderType: DeliveryOrderType;
  status: DeliveryOrderStatus;
  rejectionReason: string | null;
  customerName: string;
  customerPhone: string;
  customerEmail: string | null;
  customerId: string | null;
  deliveryAddressJson: string | null;
  deliveryFee: number;
  itemsSubtotal: number;
  total: number;
  estimatedMinutes: number | null;
  riderName: string | null;
  riderPhone: string | null;
  restOrderId: string | null;
  externalOrderId: string | null;
  notes: string | null;
  receivedAt: string;
  acceptedAt: string | null;
  readyAt: string | null;
  dispatchedAt: string | null;
  deliveredAt: string | null;
  cancelledAt: string | null;
  items: DeliveryItemDto[];
}

export interface AcceptDeliveryRequest {
  estimatedMinutes?: number | null;
}

export interface RejectDeliveryRequest {
  reason?: string | null;
}

export interface UpdateDeliveryStatusRequest {
  status: "OutForDelivery" | "Delivered";
  riderName?: string | null;
  riderPhone?: string | null;
}

export interface AssignRiderRequest {
  name: string;
  phone?: string | null;
}

export interface CreateManualItemRequest {
  productId: string;
  quantity: number;
  notes?: string | null;
}

export interface CreateManualDeliveryRequest {
  orderType: string;
  customerName: string;
  customerPhone: string;
  customerEmail?: string | null;
  deliveryAddressJson?: string | null;
  estimatedMinutes?: number | null;
  notes?: string | null;
  channel?: string;
  items?: CreateManualItemRequest[];
}

// ── Kitchen ───────────────────────────────────────────────────────────────────
export type ConnectionMode = "realtime" | "polling";

export interface KitchenItem {
  orderId: string;
  orderNumber: number;
  tableNumber: string | null;
  orderType: OrderType;
  itemId: string;
  productName: string;
  quantity: number;
  notes: string | null;
  modifiers: OrderItemModifierDto[];
  status: OrderItemStatus;
  sentToKitchenAt: string | null;
}
