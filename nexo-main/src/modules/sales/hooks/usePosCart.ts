import { useState, useMemo } from "react";
import type { CartItem, ProductSearchResult } from "../types";

export type DiscountMode = "amount" | "percentage";

export function usePosCart() {
  const [items, setItems] = useState<CartItem[]>([]);
  const [discountMode, setDiscountModeState] = useState<DiscountMode>("amount");
  /**
   * Raw user-entered value — meaning depends on discountMode:
   *   "amount"     → R$ value to subtract from the subtotal
   *   "percentage" → percentage (0–100) of the subtotal to subtract
   */
  const [discountValue, setDiscountValueState] = useState(0);

  // ── Item operations ───────────────────────────────────────────────────────

  function addItem(product: ProductSearchResult, qty = 1) {
    setItems((prev) => {
      const existing = prev.find((i) => i.productId === product.id);
      if (existing) {
        return prev.map((i) =>
          i.productId === product.id
            ? {
                ...i,
                quantity: i.quantity + qty,
                totalPrice: (i.quantity + qty) * i.unitPrice,
              }
            : i
        );
      }
      const newItem: CartItem = {
        productId: product.id,
        code: product.code,
        description: product.description,
        quantity: qty,
        unitPrice: product.price,
        discount: 0,
        totalPrice: qty * product.price,
      };
      return [...prev, newItem];
    });
  }

  function removeItem(productId: string) {
    setItems((prev) => prev.filter((i) => i.productId !== productId));
  }

  function updateQuantity(productId: string, quantity: number) {
    if (quantity <= 0) {
      removeItem(productId);
      return;
    }
    setItems((prev) =>
      prev.map((i) =>
        i.productId === productId
          ? { ...i, quantity, totalPrice: quantity * i.unitPrice }
          : i
      )
    );
  }

  // ── Discount operations ───────────────────────────────────────────────────

  /** Switch mode and reset the raw value to prevent cross-mode confusion. */
  function setDiscountMode(mode: DiscountMode) {
    setDiscountModeState(mode);
    setDiscountValueState(0);
  }

  /**
   * Set the raw discount value entered by the user.
   * Bounds enforced here:
   *   "amount"     → clamped to [0, ∞)  — subtotal cap applied in discountTotal
   *   "percentage" → clamped to [0, 100]
   */
  function setDiscountValue(value: number) {
    if (discountMode === "percentage") {
      setDiscountValueState(Math.min(100, Math.max(0, value)));
    } else {
      setDiscountValueState(Math.max(0, value));
    }
  }

  function clearCart() {
    setItems([]);
    setDiscountModeState("amount");
    setDiscountValueState(0);
  }

  // ── Derived values ────────────────────────────────────────────────────────

  const subtotal = useMemo(
    () => items.reduce((acc, i) => acc + i.unitPrice * i.quantity, 0),
    [items]
  );

  /**
   * Effective discount in R$ — the value the sale service and totals consume.
   * Always bounded to [0, subtotal] so the total never goes negative.
   */
  const discountTotal = useMemo(() => {
    if (discountMode === "amount") {
      return Math.min(discountValue, subtotal);
    }
    // percentage: derive R$ amount then cap at subtotal
    return Math.min((discountValue / 100) * subtotal, subtotal);
  }, [discountMode, discountValue, subtotal]);

  const total = useMemo(
    () => Math.max(0, subtotal - discountTotal),
    [subtotal, discountTotal]
  );

  const itemCount = useMemo(
    () => items.reduce((acc, i) => acc + i.quantity, 0),
    [items]
  );

  return {
    // State
    items,
    discountMode,
    discountValue,
    // Computed
    subtotal,
    discountTotal,  // effective R$ discount — always pass this to the sale service
    total,
    itemCount,
    // Actions
    addItem,
    removeItem,
    updateQuantity,
    setDiscountMode,
    setDiscountValue,
    clearCart,
  };
}
