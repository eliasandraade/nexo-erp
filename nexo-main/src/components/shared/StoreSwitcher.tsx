import { Store, ChevronDown, Check, Loader2 } from "lucide-react";
import { useState } from "react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useMyStores } from "@/modules/stores/hooks/useMyStores";

export function StoreSwitcher() {
  const { session, switchStore } = useAuth();
  const { data: stores, isLoading } = useMyStores();
  const [switching, setSwitching] = useState(false);

  const activeStore = stores?.find((s) => s.id === session?.storeId);
  const displayName = activeStore?.name ?? "Loja";

  async function handleSwitch(storeId: string) {
    if (storeId === session?.storeId || switching) return;
    setSwitching(true);
    try {
      await switchStore(storeId);
    } finally {
      setSwitching(false);
    }
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          className="flex items-center gap-2 text-sm font-medium text-foreground hover:text-secondary transition-colors disabled:opacity-50"
          disabled={switching}
        >
          {switching ? (
            <Loader2 className="h-4 w-4 text-muted-foreground animate-spin" />
          ) : (
            <Store className="h-4 w-4 text-muted-foreground" />
          )}
          <span>{displayName}</span>
          <ChevronDown className="h-3 w-3 text-muted-foreground" />
        </button>
      </DropdownMenuTrigger>

      <DropdownMenuContent align="start" className="w-56">
        <DropdownMenuLabel>Lojas</DropdownMenuLabel>
        <DropdownMenuSeparator />
        {isLoading && (
          <div className="flex items-center gap-2 px-2 py-1.5 text-sm text-muted-foreground">
            <Loader2 className="h-3 w-3 animate-spin" />
            Carregando...
          </div>
        )}
        {stores?.map((store) => (
          <DropdownMenuItem
            key={store.id}
            onClick={() => handleSwitch(store.id)}
            className="cursor-pointer"
          >
            <div className="flex items-center justify-between w-full">
              <span>{store.name}</span>
              {store.id === session?.storeId && (
                <Check className="h-3.5 w-3.5 text-secondary" />
              )}
            </div>
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
