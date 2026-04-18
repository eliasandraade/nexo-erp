import { useState } from "react";
import { Menu, X } from "lucide-react";

const SIGNUP_URL = "https://app.orken.com.br/auth/register";

const NAV_LINKS = [
  { label: "Como funciona", href: "#como-funciona" },
  { label: "Restaurante",   href: "#restaurante"   },
  { label: "Varejo",        href: "#varejo"         },
  { label: "Sobre",         href: "#sobre"          },
];

export function LandingNav() {
  const [open, setOpen] = useState(false);

  return (
    <header className="sticky top-0 z-50 w-full border-b border-white/10 bg-orken-navy/95 backdrop-blur-sm">
      <div className="max-w-6xl mx-auto px-5 md:px-8 h-16 flex items-center justify-between">

        {/* Logo */}
        <a href="/" className="flex items-center gap-2 shrink-0">
          <span className="text-xl font-bold tracking-tight text-white">
            Ork<span className="text-orken-indigo">en</span>
          </span>
        </a>

        {/* Desktop nav */}
        <nav className="hidden md:flex items-center gap-8">
          {NAV_LINKS.map((l) => (
            <a
              key={l.href}
              href={l.href}
              className="text-sm text-slate-400 hover:text-white transition-colors"
            >
              {l.label}
            </a>
          ))}
        </nav>

        {/* Desktop CTA */}
        <a
          href={SIGNUP_URL}
          className="hidden md:inline-flex items-center gap-1.5 bg-orken-indigo hover:bg-orken-indigo-dark text-white text-sm font-semibold px-5 py-2.5 rounded-lg transition-colors"
        >
          Criar minha conta →
        </a>

        {/* Mobile hamburger */}
        <button
          className="md:hidden text-slate-400 hover:text-white p-1"
          onClick={() => setOpen((v) => !v)}
          aria-label="Menu"
        >
          {open ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
        </button>
      </div>

      {/* Mobile menu */}
      {open && (
        <div className="md:hidden border-t border-white/10 bg-orken-navy px-5 py-4 space-y-3">
          {NAV_LINKS.map((l) => (
            <a
              key={l.href}
              href={l.href}
              onClick={() => setOpen(false)}
              className="block text-sm text-slate-300 hover:text-white py-1.5"
            >
              {l.label}
            </a>
          ))}
          <a
            href={SIGNUP_URL}
            className="block mt-3 text-center bg-orken-indigo hover:bg-orken-indigo-dark text-white text-sm font-semibold px-5 py-3 rounded-lg transition-colors"
          >
            Criar minha conta →
          </a>
        </div>
      )}
    </header>
  );
}
