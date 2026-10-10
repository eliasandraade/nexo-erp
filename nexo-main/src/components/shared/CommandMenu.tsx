import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Search } from "lucide-react";
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { useNavigation } from "./useNavigation";

export function CommandMenu() {
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const routes = useNavigation();
  const navigate = useNavigate();
  useEffect(() => {
    const handle = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault(); setOpen(value => !value);
      }
    };
    document.addEventListener("keydown", handle);
    return () => document.removeEventListener("keydown", handle);
  }, []);
  return <>
    <button ref={trigger} onClick={() => setOpen(true)} className="ml-auto flex h-9 items-center gap-2 rounded border border-border px-3 text-sm text-muted-foreground hover:bg-muted" aria-label="Buscar páginas" aria-haspopup="dialog">
      <Search className="h-4 w-4" /><span className="hidden sm:inline">Ir para…</span><kbd className="hidden text-xs lg:inline">Ctrl K</kbd>
    </button>
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogContent className="overflow-hidden p-0" onCloseAutoFocus={event => { event.preventDefault(); trigger.current?.focus(); }}>
        <DialogTitle className="sr-only">Buscar páginas</DialogTitle>
        <DialogDescription className="sr-only">Destinos disponíveis na sua área de trabalho. Use as setas e Enter para abrir.</DialogDescription>
        <Command>
          <CommandInput placeholder="Qual página você quer abrir?" aria-label="Nome da página" />
          <CommandList>
            <CommandEmpty>Nenhuma página disponível com esse nome.</CommandEmpty>
            <CommandGroup heading="Nesta área de trabalho">
              {routes.map(route => <CommandItem key={route.path} value={`${route.label} ${route.path}`} onSelect={() => { setOpen(false); navigate(route.path); }}>
                <span className="flex-1 py-1">{route.label}</span><span className="text-xs text-muted-foreground">Abrir</span>
              </CommandItem>)}
            </CommandGroup>
          </CommandList>
        </Command>
      </DialogContent>
    </Dialog>
  </>;
}
