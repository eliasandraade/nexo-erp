import { useState, useRef, useEffect, useMemo } from "react";
import { Search, Plus } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { useProducts } from "@/modules/products/hooks/use-products";
import { useStockItems } from "@/modules/inventory/hooks/use-stock";
import type { ProductSearchResult } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface PosProductSearchProps {
  onAdd: (product: ProductSearchResult) => void;
}

/**
 * Searches active products from the real backend API.
 * Products and stock are fetched once and filtered client-side for speed.
 * Enter → adds first result. Escape → clears. Barcode scanners work natively
 * (they send characters fast + Enter at the end).
 */
export function PosProductSearch({ onAdd }: PosProductSearchProps) {
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const { data: products = [] } = useProducts(false);          // active only
  const { data: stockItems = [] } = useStockItems();

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  /** Build a map from productId → current quantity for O(1) stock lookup. */
  const stockMap = useMemo(() => {
    const map = new Map<string, number>();
    for (const s of stockItems) {
      map.set(s.productId, s.currentQuantity ?? 0);
    }
    return map;
  }, [stockItems]);

  const results = useMemo((): ProductSearchResult[] => {
    const q = query.trim().toLowerCase();
    if (!q) return [];

    return products
      .filter((p) => p.isActive)
      .filter(
        (p) =>
          p.code.toLowerCase().includes(q) ||
          (p.barcode ?? "").toLowerCase().includes(q) ||
          p.name.toLowerCase().includes(q)
      )
      .sort((a, b) => {
        // Exact code/barcode match first, then prefix, then contains
        const aExact  = a.code.toLowerCase() === q || (a.barcode ?? "").toLowerCase() === q;
        const bExact  = b.code.toLowerCase() === q || (b.barcode ?? "").toLowerCase() === q;
        if (aExact && !bExact) return -1;
        if (!aExact && bExact) return 1;
        return a.name.localeCompare(b.name);
      })
      .slice(0, 8)
      .map((p) => ({
        id:          p.id,
        code:        p.code,
        description: p.name,
        price:       p.salePrice,
        unit:        p.unit,
        stock:       stockMap.get(p.id) ?? 0,
      }));
  }, [query, products, stockMap]);

  function handleSearch(value: string) {
    setQuery(value);
    setOpen(value.trim().length > 0 && results.length > 0);
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Enter") {
      if (results.length > 0) selectProduct(results[0]);
    } else if (e.key === "Escape") {
      setOpen(false);
      setQuery("");
    }
  }

  function selectProduct(product: ProductSearchResult) {
    onAdd(product);
    setQuery("");
    setOpen(false);
    setTimeout(() => inputRef.current?.focus(), 50);
  }

  // Sync open state when results change
  useEffect(() => {
    setOpen(query.trim().length > 0 && results.length > 0);
  }, [results, query]);

  return (
    <div className="relative">
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
        <Input
          ref={inputRef}
          placeholder="Código, código de barras ou nome... (Enter para adicionar)"
          value={query}
          onChange={(e) => handleSearch(e.target.value)}
          onKeyDown={handleKeyDown}
          onBlur={() => setTimeout(() => setOpen(false), 150)}
          onFocus={() => query.trim().length > 0 && results.length > 0 && setOpen(true)}
          className="pl-9"
        />
      </div>

      {open && results.length > 0 && (
        <div className="absolute top-full left-0 right-0 z-50 mt-1 bg-popover border border-border rounded-md shadow-md overflow-hidden">
          {results.map((product) => (
            <button
              key={product.id}
              type="button"
              className="w-full flex items-center justify-between px-3 py-2.5 text-sm hover:bg-accent transition-colors text-left gap-2"
              onMouseDown={() => selectProduct(product)}
            >
              <div className="min-w-0">
                <span className="text-xs text-muted-foreground font-mono mr-2">
                  {product.code}
                </span>
                <span className="font-medium">{product.description}</span>
                {product.stock === 0 && (
                  <span className="ml-2 text-xs text-red-500">(sem estoque)</span>
                )}
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <span className="font-semibold text-foreground">
                  {formatCurrency(product.price)}
                </span>
                <Button size="sm" variant="ghost" className="h-6 w-6 p-0" tabIndex={-1}>
                  <Plus className="h-3.5 w-3.5" />
                </Button>
              </div>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
