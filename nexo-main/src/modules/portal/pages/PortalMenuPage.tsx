import { useState } from "react";
import { useParams } from "react-router-dom";
import { ShoppingCart, AlertCircle } from "lucide-react";
import { cn } from "@/lib/utils";
import { usePublicMenu } from "../hooks/usePublicMenu";
import { ProductSheet } from "../components/ProductSheet";
import { CartSheet } from "../components/CartSheet";
import type { PublicMenuProductDto, CartItem, CartModifier } from "../types";

export default function PortalMenuPage() {
  const { slug = "" } = useParams<{ slug: string }>();
  const { data: menu, isLoading, isError } = usePublicMenu(slug);

  const [activeProduct, setActiveProduct] = useState<PublicMenuProductDto | null>(null);
  const [cartOpen,      setCartOpen]      = useState(false);
  const [cart,          setCart]          = useState<CartItem[]>([]);
  const [activeCategory, setActiveCategory] = useState<string | null>(null);

  const cartTotal = cart.reduce(
    (s, i) => s + (i.price + i.modifiers.reduce((ms: number, m: CartModifier) => ms + m.price, 0)) * i.quantity,
    0
  );
  const cartCount = cart.reduce((s, i) => s + i.quantity, 0);

  const addToCart = (item: CartItem) => {
    setCart((prev) => {
      const idx = prev.findIndex((i) => i.productId === item.productId);
      if (idx >= 0) {
        const next = [...prev];
        next[idx] = { ...next[idx], quantity: next[idx].quantity + item.quantity };
        return next;
      }
      return [...prev, item];
    });
  };

  const changeQty = (productId: string, delta: number) => {
    setCart((prev) =>
      prev
        .map((i) => i.productId === productId ? { ...i, quantity: i.quantity + delta } : i)
        .filter((i) => i.quantity > 0)
    );
  };

  const removeFromCart = (productId: string) => {
    setCart((prev) => prev.filter((i) => i.productId !== productId));
  };

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-2 border-primary border-t-transparent rounded-full animate-spin" />
          <p className="text-sm text-muted-foreground">Carregando cardápio...</p>
        </div>
      </div>
    );
  }

  if (isError || !menu) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center p-6">
        <div className="flex flex-col items-center gap-3 text-center max-w-xs">
          <AlertCircle className="h-10 w-10 text-muted-foreground" />
          <p className="font-semibold">Restaurante não encontrado</p>
          <p className="text-sm text-muted-foreground">
            O link pode estar incorreto ou o restaurante não está disponível.
          </p>
        </div>
      </div>
    );
  }

  const displayCategories = (activeCategory
    ? menu.categories.filter((c) => (c.id ?? "outros") === activeCategory)
    : menu.categories
  );

  return (
    <div className="min-h-screen bg-background text-foreground">
      {/* ── Header / Cover ── */}
      <div className="relative">
        {menu.coverImageUrl ? (
          <div className="w-full h-40 overflow-hidden">
            <img src={menu.coverImageUrl} alt={menu.storeName} className="w-full h-full object-cover" />
          </div>
        ) : (
          <div className="w-full h-24 bg-gradient-to-r from-primary/30 to-primary/10" />
        )}
        <div className="px-4 pt-3 pb-4">
          {menu.logoUrl && (
            <img
              src={menu.logoUrl}
              alt={menu.storeName}
              className="w-16 h-16 rounded-xl object-cover border-2 border-background -mt-8 mb-2 shadow-md"
            />
          )}
          <h1 className="text-xl font-bold">{menu.storeName}</h1>
          {menu.description && (
            <p className="text-sm text-muted-foreground mt-0.5">{menu.description}</p>
          )}
          {!menu.acceptingOrders && (
            <div className="mt-2 flex items-center gap-2 rounded-lg bg-amber-950/40 border border-amber-700/40 px-3 py-2">
              <AlertCircle className="h-4 w-4 text-amber-400 shrink-0" />
              <p className="text-sm text-amber-300">Fechado no momento — não aceitando pedidos.</p>
            </div>
          )}
        </div>
      </div>

      {/* ── Category tabs ── */}
      {menu.categories.length > 1 && (
        <div className="sticky top-0 z-10 bg-background border-b border-border overflow-x-auto">
          <div className="flex gap-1 px-4 py-2 min-w-max">
            <button
              onClick={() => setActiveCategory(null)}
              className={cn(
                "px-3 py-1.5 rounded-full text-sm font-medium whitespace-nowrap transition-colors",
                activeCategory === null
                  ? "bg-primary text-primary-foreground"
                  : "text-muted-foreground hover:text-foreground"
              )}
            >
              Tudo
            </button>
            {menu.categories.map((cat) => (
              <button
                key={cat.id ?? "outros"}
                onClick={() => setActiveCategory(cat.id ?? "outros")}
                className={cn(
                  "px-3 py-1.5 rounded-full text-sm font-medium whitespace-nowrap transition-colors",
                  activeCategory === (cat.id ?? "outros")
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:text-foreground"
                )}
              >
                {cat.name}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* ── Product list ── */}
      <div className="px-4 pb-32">
        {displayCategories.map((cat) => (
          <div key={cat.id ?? "outros"} className="mt-6">
            <h2 className="text-base font-semibold mb-3">{cat.name}</h2>
            <div className="flex flex-col gap-2">
              {cat.products.map((product) => (
                <button
                  key={product.id}
                  onClick={() => menu.acceptingOrders && setActiveProduct(product)}
                  className={cn(
                    "flex items-start gap-3 rounded-xl border border-border bg-card p-3 text-left transition-colors",
                    menu.acceptingOrders ? "hover:border-primary/40 hover:bg-primary/5" : "opacity-60 cursor-default"
                  )}
                >
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium leading-tight">{product.name}</p>
                    {product.description && (
                      <p className="text-xs text-muted-foreground mt-0.5 line-clamp-2">{product.description}</p>
                    )}
                    <p className="text-sm font-bold tabular-nums mt-1">R$ {product.price.toFixed(2)}</p>
                  </div>
                  {product.imageUrl && (
                    <img
                      src={product.imageUrl}
                      alt={product.name}
                      className="w-20 h-20 rounded-lg object-cover shrink-0"
                    />
                  )}
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>

      {/* ── Cart FAB ── */}
      {cartCount > 0 && (
        <div className="fixed bottom-6 left-4 right-4 z-20">
          <button
            onClick={() => setCartOpen(true)}
            className="w-full flex items-center gap-3 rounded-xl bg-primary px-4 py-3.5 text-primary-foreground shadow-lg"
          >
            <div className="relative">
              <ShoppingCart className="h-5 w-5" />
              <span className="absolute -top-2 -right-2 bg-primary-foreground text-primary text-[10px] font-bold rounded-full w-4 h-4 flex items-center justify-center">
                {cartCount}
              </span>
            </div>
            <span className="flex-1 text-sm font-semibold text-left">Ver pedido</span>
            <span className="text-sm font-bold tabular-nums">R$ {cartTotal.toFixed(2)}</span>
          </button>
        </div>
      )}

      {/* ── Product sheet ── */}
      <ProductSheet
        product={activeProduct}
        onClose={() => setActiveProduct(null)}
        onAdd={(item) => { addToCart(item); setActiveProduct(null); }}
      />

      {/* ── Cart sheet ── */}
      <CartSheet
        open={cartOpen}
        onClose={() => setCartOpen(false)}
        items={cart}
        onChangeQty={changeQty}
        onRemove={removeFromCart}
        slug={slug}
        deliveryEnabled={menu.deliveryEnabled}
        takeawayEnabled={menu.takeawayEnabled}
      />
    </div>
  );
}
