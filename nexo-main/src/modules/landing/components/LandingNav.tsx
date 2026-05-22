import { useEffect, useRef, useState } from "react";
import { ChevronDown, Menu, X } from "lucide-react";

const SIGNUP_URL = "https://app.orken.com.br/register";
const LOGIN_URL  = "https://app.orken.com.br/login";

const MODULES = [
  {
    label: "Orken Menu",
    description: "Restaurantes, mesas, KDS e delivery",
    href: "#orken-menu",
  },
  {
    label: "Orken Build",
    description: "Obras, despesas e movimentações",
    href: "#orken-build",
  },
];

export function LandingNav() {
  const [mobileOpen, setMobileOpen]   = useState(false);
  const [modulesOpen, setModulesOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  // Close modules dropdown on outside click
  useEffect(() => {
    function onClickOutside(e: MouseEvent) {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setModulesOpen(false);
      }
    }
    document.addEventListener("mousedown", onClickOutside);
    return () => document.removeEventListener("mousedown", onClickOutside);
  }, []);

  // Close mobile menu on resize to desktop
  useEffect(() => {
    function onResize() {
      if (window.innerWidth >= 768) setMobileOpen(false);
    }
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, []);

  return (
    <header className="fixed top-0 inset-x-0 z-50 border-b border-white/5 backdrop-blur-xl bg-orken-navy/90">
      <div className="max-w-6xl mx-auto px-5 md:px-8 h-[60px] flex items-center justify-between">

        {/* Logo */}
        <a href="/" className="font-display text-xl font-bold text-white tracking-tight select-none shrink-0">
          Ork<span className="text-orken-indigo">en</span>
        </a>

        {/* Desktop nav */}
        <nav className="hidden md:flex items-center gap-7 text-sm text-slate-400">
          <a href="#como-funciona" className="hover:text-white transition-colors">
            Produto
          </a>

          {/* Módulos dropdown */}
          <div ref={dropdownRef} className="relative">
            <button
              onClick={() => setModulesOpen((v) => !v)}
              className="flex items-center gap-1 hover:text-white transition-colors"
            >
              Módulos
              <ChevronDown
                className={`h-3.5 w-3.5 transition-transform duration-200 ${
                  modulesOpen ? "rotate-180" : ""
                }`}
              />
            </button>

            {modulesOpen && (
              <div className="absolute top-full mt-3 left-1/2 -translate-x-1/2 w-64 bg-[#0f1423] border border-white/8 rounded-2xl p-2 shadow-2xl animate-slide-down">
                {MODULES.map((m) => (
                  <a
                    key={m.label}
                    href={m.href}
                    onClick={() => setModulesOpen(false)}
                    className="flex flex-col gap-0.5 px-4 py-3 rounded-xl hover:bg-white/5 transition-colors group"
                  >
                    <span className="text-white text-sm font-medium group-hover:text-orken-indigo transition-colors">
                      {m.label}
                    </span>
                    <span className="text-slate-500 text-xs">{m.description}</span>
                  </a>
                ))}
              </div>
            )}
          </div>

          <a href="#precos" className="hover:text-white transition-colors">
            Preços
          </a>
        </nav>

        {/* Desktop CTAs */}
        <div className="hidden md:flex items-center gap-3">
          <a
            href={LOGIN_URL}
            className="text-sm text-slate-400 hover:text-white transition-colors px-3 py-1.5"
          >
            Entrar
          </a>
          <a
            href={SIGNUP_URL}
            className="text-sm font-semibold bg-orken-indigo hover:bg-orken-indigo/90 text-white px-4 py-2 rounded-lg transition-colors whitespace-nowrap"
          >
            Começar grátis →
          </a>
        </div>

        {/* Mobile hamburger */}
        <button
          className="md:hidden text-slate-400 hover:text-white transition-colors p-3 -mr-2"
          onClick={() => setMobileOpen((v) => !v)}
          aria-label="Menu"
        >
          {mobileOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
        </button>
      </div>

      {/* Mobile menu */}
      {mobileOpen && (
        <div className="md:hidden border-t border-white/5 bg-orken-navy animate-slide-down">
          <div className="max-w-6xl mx-auto px-5 py-6 space-y-1">
            <a
              href="#como-funciona"
              onClick={() => setMobileOpen(false)}
              className="block px-4 py-3 text-sm text-slate-400 hover:text-white rounded-xl hover:bg-white/5 transition-colors"
            >
              Produto
            </a>

            <div className="px-4 py-3">
              <p className="text-xs font-semibold uppercase tracking-widest text-slate-500 mb-2">
                Módulos
              </p>
              {MODULES.map((m) => (
                <a
                  key={m.label}
                  href={m.href}
                  onClick={() => setMobileOpen(false)}
                  className="block py-2.5 text-sm text-slate-400 hover:text-white transition-colors"
                >
                  {m.label}
                </a>
              ))}
            </div>

            <a
              href="#precos"
              onClick={() => setMobileOpen(false)}
              className="block px-4 py-3 text-sm text-slate-400 hover:text-white rounded-xl hover:bg-white/5 transition-colors"
            >
              Preços
            </a>

            <div className="pt-4 border-t border-white/5 flex flex-col gap-3">
              <a
                href={LOGIN_URL}
                className="text-center text-sm text-slate-400 hover:text-white transition-colors py-2"
              >
                Entrar
              </a>
              <a
                href={SIGNUP_URL}
                className="text-center text-sm font-semibold bg-orken-indigo hover:bg-orken-indigo/90 text-white px-4 py-3 rounded-lg transition-colors"
              >
                Começar grátis →
              </a>
            </div>
          </div>
        </div>
      )}
    </header>
  );
}
