const SIGNUP_URL = "https://app.orken.com.br/register";
const LOGIN_URL  = "https://app.orken.com.br/login";

export function LandingFooter() {
  return (
    <footer className="bg-black/40 border-t border-white/5 py-8 px-5 md:px-8">
      <div className="max-w-6xl mx-auto flex flex-col sm:flex-row items-center justify-between gap-4">

        {/* Brand */}
        <span className="text-base font-bold tracking-tight text-white/60">
          Ork<span className="text-orken-indigo">en</span>
          <span className="ml-2 text-white/25 font-normal text-sm">— Gestão operacional para negócios reais.</span>
        </span>

        {/* Links */}
        <nav className="flex items-center gap-5 text-sm text-white/40">
          <a href={LOGIN_URL}  className="hover:text-white/70 transition-colors">Entrar</a>
          <a href={SIGNUP_URL} className="hover:text-white/70 transition-colors">Criar conta</a>
          <span>© {new Date().getFullYear()} Andrade Systems</span>
        </nav>

      </div>
    </footer>
  );
}
