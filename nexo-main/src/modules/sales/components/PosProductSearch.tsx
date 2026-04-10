import { useState, useRef, useEffect } from "react";
import { Search, Plus } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { posService } from "../services/posService";
import type { ProductSearchResult } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface PosProductSearchProps {
  onAdd: (product: ProductSearchResult) => void;
}

export function PosProductSearch({ onAdd }: PosProductSearchProps) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<ProductSearchResult[]>([]);
  const [open, setOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  function handleSearch(value: string) {
    setQuery(value);
    if (value.trim().length === 0) {
      setResults([]);
      setOpen(false);
      return;
    }
    const found = posService.searchProduct(value);
    setResults(found);
    setOpen(found.length > 0);
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Enter") {
      if (results.length > 0) {
        selectProduct(results[0]);
      }
    } else if (e.key === "Escape") {
      setOpen(false);
      setQuery("");
    }
  }

  function selectProduct(product: ProductSearchResult) {
    onAdd(product);
    setQuery("");
    setResults([]);
    setOpen(false);
    setTimeout(() => inputRef.current?.focus(), 50);
  }

  return (
    <div className="relative">
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
        <Input
          ref={inputRef}
          placeholder="Código de barras ou nome do produto... (Enter para adicionar)"
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
          {results.slice(0, 8).map((product) => (
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
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-6 w-6 p-0"
                  tabIndex={-1}
                >
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
