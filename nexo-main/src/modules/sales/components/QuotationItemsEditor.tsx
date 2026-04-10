import { useState, useRef, useEffect } from "react";
import { Plus, Trash2 } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { fetchProducts } from "@/modules/products/api/products.api";
import { formatCurrency } from "@/lib/formatters";
import type { QuotationItem } from "../types/quotation";
import type { ProductDto } from "@/modules/products/types";

interface QuotationItemsEditorProps {
  items: QuotationItem[];
  onChange: (items: QuotationItem[]) => void;
}

interface AddRow {
  productId: string;
  code: string;
  description: string;
  unitPrice: number;
  quantity: string;
  discount: string;
}

const EMPTY_ADD_ROW: AddRow = {
  productId: "",
  code: "",
  description: "",
  unitPrice: 0,
  quantity: "1",
  discount: "0",
};

export function QuotationItemsEditor({ items, onChange }: QuotationItemsEditorProps) {
  const [search, setSearch] = useState("");
  const [showDropdown, setShowDropdown] = useState(false);
  const [addRow, setAddRow] = useState<AddRow>(EMPTY_ADD_ROW);
  const searchRef = useRef<HTMLInputElement>(null);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const { data: products = [] } = useQuery<ProductDto[]>({
    queryKey: ["products"],
    queryFn: () => fetchProducts(),
    staleTime: 60_000,
  });

  const filtered = search.trim()
    ? products
        .filter(
          (p) =>
            p.name.toLowerCase().includes(search.toLowerCase()) ||
            p.code.toLowerCase().includes(search.toLowerCase())
        )
        .slice(0, 8)
    : [];

  // Close dropdown on outside click
  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (
        dropdownRef.current &&
        !dropdownRef.current.contains(e.target as Node) &&
        searchRef.current &&
        !searchRef.current.contains(e.target as Node)
      ) {
        setShowDropdown(false);
      }
    }
    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  }, []);

  function selectProduct(product: ProductDto) {
    setAddRow({
      productId: product.id,
      code: product.code,
      description: product.name,
      unitPrice: product.salePrice,
      quantity: "1",
      discount: "0",
    });
    setSearch(product.name);
    setShowDropdown(false);
  }

  function handleAddItem() {
    if (!addRow.productId) return;
    const qty = Math.max(1, Number(addRow.quantity) || 1);
    const disc = Math.max(0, Number(addRow.discount) || 0);
    const totalPrice = addRow.unitPrice * qty - disc;

    const newItem: QuotationItem = {
      id: crypto.randomUUID(),
      productId: addRow.productId,
      code: addRow.code,
      description: addRow.description,
      quantity: qty,
      unitPrice: addRow.unitPrice,
      discount: disc,
      totalPrice,
    };

    onChange([...items, newItem]);
    setAddRow(EMPTY_ADD_ROW);
    setSearch("");
    searchRef.current?.focus();
  }

  function handleRemoveItem(id: string) {
    onChange(items.filter((i) => i.id !== id));
  }

  function handleItemChange(
    id: string,
    field: "quantity" | "discount",
    raw: string
  ) {
    onChange(
      items.map((i) => {
        if (i.id !== id) return i;
        const qty = field === "quantity" ? Math.max(1, Number(raw) || 1) : i.quantity;
        const disc = field === "discount" ? Math.max(0, Number(raw) || 0) : i.discount;
        return {
          ...i,
          quantity: qty,
          discount: disc,
          totalPrice: i.unitPrice * qty - disc,
        };
      })
    );
  }

  return (
    <div className="space-y-4">
      {/* Product search + add row */}
      <div className="flex flex-wrap gap-3 items-end">
        <div className="relative flex-1 min-w-56">
          <label className="text-xs text-muted-foreground mb-1 block">
            Buscar produto
          </label>
          <Input
            ref={searchRef}
            placeholder="Código ou descrição..."
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setShowDropdown(true);
              if (!e.target.value) {
                setAddRow(EMPTY_ADD_ROW);
              }
            }}
            onFocus={() => search && setShowDropdown(true)}
            autoComplete="off"
          />
          {showDropdown && filtered.length > 0 && (
            <div
              ref={dropdownRef}
              className="absolute z-50 top-full mt-1 left-0 right-0 bg-background border border-border rounded-md shadow-lg max-h-64 overflow-y-auto"
            >
              {filtered.map((p) => (
                <button
                  key={p.id}
                  type="button"
                  className="w-full flex justify-between items-center px-3 py-2 text-sm hover:bg-muted/60 text-left"
                  onMouseDown={(e) => {
                    e.preventDefault();
                    selectProduct(p);
                  }}
                >
                  <span>
                    <span className="font-mono text-xs text-muted-foreground mr-2">
                      {p.code}
                    </span>
                    {p.name}
                  </span>
                  <span className="text-muted-foreground tabular-nums ml-4 shrink-0">
                    {formatCurrency(p.salePrice)}
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>

        <div className="w-24">
          <label className="text-xs text-muted-foreground mb-1 block">Qtd.</label>
          <Input
            type="number"
            min={1}
            value={addRow.quantity}
            onChange={(e) => setAddRow((r) => ({ ...r, quantity: e.target.value }))}
            className="text-center"
          />
        </div>

        <div className="w-28">
          <label className="text-xs text-muted-foreground mb-1 block">
            Preço unit.
          </label>
          <Input
            type="number"
            min={0}
            step={0.01}
            value={addRow.unitPrice || ""}
            onChange={(e) =>
              setAddRow((r) => ({ ...r, unitPrice: Number(e.target.value) || 0 }))
            }
            className="text-right"
          />
        </div>

        <div className="w-28">
          <label className="text-xs text-muted-foreground mb-1 block">
            Desconto (R$)
          </label>
          <Input
            type="number"
            min={0}
            step={0.01}
            value={addRow.discount}
            onChange={(e) => setAddRow((r) => ({ ...r, discount: e.target.value }))}
            className="text-right"
          />
        </div>

        <Button
          type="button"
          onClick={handleAddItem}
          disabled={!addRow.productId}
          className="gap-2"
        >
          <Plus className="h-4 w-4" />
          Adicionar
        </Button>
      </div>

      {/* Items table */}
      {items.length > 0 ? (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-28">Código</TableHead>
              <TableHead>Descrição</TableHead>
              <TableHead className="w-24 text-center">Qtd.</TableHead>
              <TableHead className="w-32 text-right">Preço unit.</TableHead>
              <TableHead className="w-32 text-right">Desconto</TableHead>
              <TableHead className="w-32 text-right">Total</TableHead>
              <TableHead className="w-10" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((item) => (
              <TableRow key={item.id}>
                <TableCell className="font-mono text-xs text-muted-foreground">
                  {item.code}
                </TableCell>
                <TableCell className="text-sm">{item.description}</TableCell>
                <TableCell>
                  <Input
                    type="number"
                    min={1}
                    value={item.quantity}
                    onChange={(e) =>
                      handleItemChange(item.id, "quantity", e.target.value)
                    }
                    className="w-20 text-center mx-auto"
                  />
                </TableCell>
                <TableCell className="text-right text-sm tabular-nums">
                  {formatCurrency(item.unitPrice)}
                </TableCell>
                <TableCell>
                  <Input
                    type="number"
                    min={0}
                    step={0.01}
                    value={item.discount}
                    onChange={(e) =>
                      handleItemChange(item.id, "discount", e.target.value)
                    }
                    className="w-28 text-right ml-auto"
                  />
                </TableCell>
                <TableCell className="text-right text-sm font-semibold tabular-nums">
                  {formatCurrency(item.totalPrice)}
                </TableCell>
                <TableCell>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="h-7 w-7 p-0 text-destructive hover:text-destructive"
                    onClick={() => handleRemoveItem(item.id)}
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      ) : (
        <div className="text-sm text-muted-foreground italic py-4 text-center border border-dashed border-border rounded-md">
          Nenhum item adicionado. Use a busca acima para adicionar produtos.
        </div>
      )}
    </div>
  );
}
