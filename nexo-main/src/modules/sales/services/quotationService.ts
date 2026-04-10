import type {
  Quotation,
  QuotationFormInput,
  QuotationListFilters,
} from "../types/quotation";
import { mockQuotations } from "../data/mockQuotations";

const delay = (ms = 400) => new Promise<void>((r) => setTimeout(r, ms));

/** In-memory store so mutations persist within the session */
let store: Quotation[] = [...mockQuotations];
let nextId = mockQuotations.length + 1;

function calcTotals(items: QuotationFormInput["items"]) {
  const subtotal = items.reduce(
    (sum, i) => sum + i.unitPrice * i.quantity,
    0
  );
  const discountTotal = items.reduce((sum, i) => sum + i.discount, 0);
  const total = subtotal - discountTotal;
  return { subtotal, discountTotal, total };
}

/**
 * Quotation service — frontend-only, in-memory persistence.
 *
 * Future integration path:
 *   list         → GET /api/quotations?...
 *   getById      → GET /api/quotations/:id
 *   create       → POST /api/quotations
 *   update       → PUT /api/quotations/:id
 *   convertToSale → POST /api/quotations/:id/convert
 */
export const quotationService = {
  async list(filters?: Partial<QuotationListFilters>): Promise<Quotation[]> {
    await delay(300);
    let result = [...store];

    if (filters?.status && filters.status !== "all") {
      result = result.filter((q) => q.status === filters.status);
    }

    if (filters?.operator && filters.operator !== "all") {
      result = result.filter((q) => q.operator === filters.operator);
    }

    if (filters?.search) {
      const q = filters.search.toLowerCase();
      result = result.filter(
        (o) =>
          o.id.toLowerCase().includes(q) ||
          (o.customerName?.toLowerCase().includes(q) ?? false) ||
          o.operator.toLowerCase().includes(q)
      );
    }

    return result;
  },

  async getById(id: string): Promise<Quotation | null> {
    await delay(300);
    return store.find((q) => q.id === id) ?? null;
  },

  async create(input: QuotationFormInput): Promise<Quotation> {
    await delay(500);
    const { subtotal, discountTotal, total } = calcTotals(input.items);
    const now = new Date().toISOString();
    const id = `orc-${String(nextId++).padStart(4, "0")}`;
    const quotation: Quotation = {
      id,
      createdAt: now,
      updatedAt: now,
      createdBy: input.operator,
      operator: input.operator,
      customerId: input.customerId || null,
      customerName: input.customerName || null,
      status: input.status,
      notes: input.notes,
      items: input.items,
      subtotal,
      discountTotal,
      total,
    };
    store = [quotation, ...store];
    return quotation;
  },

  async update(id: string, input: Partial<QuotationFormInput>): Promise<Quotation> {
    await delay(500);
    const existing = store.find((q) => q.id === id);
    if (!existing) throw new Error(`Quotation ${id} not found`);

    const updatedItems = input.items ?? existing.items;
    const { subtotal, discountTotal, total } = calcTotals(updatedItems);

    const updated: Quotation = {
      ...existing,
      operator: input.operator ?? existing.operator,
      customerId:
        input.customerId !== undefined
          ? input.customerId || null
          : existing.customerId,
      customerName:
        input.customerName !== undefined
          ? input.customerName || null
          : existing.customerName,
      status: input.status ?? existing.status,
      notes: input.notes ?? existing.notes,
      items: updatedItems,
      subtotal,
      discountTotal,
      total,
      updatedAt: new Date().toISOString(),
    };

    store = store.map((q) => (q.id === id ? updated : q));
    return updated;
  },

  /**
   * Future implementation: creates a CompletedSale from this quotation,
   * marks the quotation as "converted", and links the IDs.
   *
   * Architecture notes:
   * - Should call posService.finalizeSale() or a dedicated salesService.createFromQuotation()
   * - Must update inventory and cash session
   * - The quotation's status → "converted"; convertedToSaleId and convertedAt are set
   * - Payment method selection will be required before conversion
   */
  async convertToSale(_id: string): Promise<never> {
    throw new Error(
      "convertToSale is not yet implemented. " +
        "This will POST /api/quotations/:id/convert and create a CompletedSale."
    );
  },

  /** Returns distinct operator names from the current store */
  async listOperators(): Promise<string[]> {
    await delay(100);
    return [...new Set(store.map((q) => q.operator))].sort();
  },
};
