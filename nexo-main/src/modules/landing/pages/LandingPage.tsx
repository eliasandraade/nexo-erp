import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  ShoppingCart,
  Package,
  Landmark,
  TrendingUp,
  Store,
  Users,
  ArrowRight,
  CheckCircle2,
  ShoppingBag,
  BarChart3,
} from "lucide-react";

// ─── Navbar ──────────────────────────────────────────────────────────────────

function LandingNav() {
  return (
    <header className="fixed top-0 left-0 right-0 z-50 bg-[hsl(222,47%,11%)]/95 backdrop-blur border-b border-white/5">
      <div className="max-w-6xl mx-auto px-6 h-14 flex items-center justify-between">
        <span className="text-white font-semibold text-base tracking-tight">
          Nexo<span className="text-[hsl(217,91%,60%)]">ERP</span>
        </span>
        <nav className="flex items-center gap-3">
          <Button asChild variant="ghost" size="sm" className="text-white/70 hover:text-white hover:bg-white/10">
            <Link to="/login">Entrar</Link>
          </Button>
          <Button asChild size="sm" className="bg-[hsl(217,91%,60%)] hover:bg-[hsl(217,91%,55%)] text-white border-0">
            <Link to="/register">Criar conta grátis</Link>
          </Button>
        </nav>
      </div>
    </header>
  );
}

// ─── Hero ─────────────────────────────────────────────────────────────────────

function HeroSection() {
  return (
    <section className="bg-[hsl(222,47%,11%)] pt-28 pb-24 px-6">
      <div className="max-w-3xl mx-auto text-center space-y-8">
        <Badge
          variant="outline"
          className="border-white/20 text-white/60 bg-white/5 text-xs font-normal px-3 py-1"
        >
          Em beta — primeiros lojistas entrando agora
        </Badge>

        <h1 className="text-4xl sm:text-5xl font-bold text-white leading-tight tracking-tight">
          Seu negócio organizado.
          <br />
          <span className="text-[hsl(217,91%,60%)]">Do estoque ao caixa.</span>
        </h1>

        <p className="text-lg text-white/60 max-w-xl mx-auto leading-relaxed">
          O Nexo conecta PDV, estoque, compras e financeiro em um sistema
          simples — sem planilhas, sem gambiarras.
        </p>

        <div className="flex flex-col sm:flex-row items-center justify-center gap-3 pt-2">
          <Button
            asChild
            size="lg"
            className="bg-[hsl(217,91%,60%)] hover:bg-[hsl(217,91%,55%)] text-white border-0 px-8 text-base font-medium"
          >
            <Link to="/register">
              Criar conta grátis
              <ArrowRight className="ml-2 h-4 w-4" />
            </Link>
          </Button>
          <p className="text-sm text-white/40">
            Sem cartão de crédito. Pronto em minutos.
          </p>
        </div>
      </div>
    </section>
  );
}

// ─── Problema ─────────────────────────────────────────────────────────────────

const problems = [
  {
    icon: "📊",
    text: "Controlo o estoque numa planilha que ninguém mais entende.",
  },
  {
    icon: "💸",
    text: "Não sei se estou lucrando de verdade no final do mês.",
  },
  {
    icon: "🔌",
    text: "Meu PDV não conversa com o estoque. Tudo é manual.",
  },
];

function ProblemSection() {
  return (
    <section className="bg-muted/50 py-20 px-6 border-b border-border">
      <div className="max-w-4xl mx-auto">
        <p className="text-center text-sm font-medium text-muted-foreground uppercase tracking-widest mb-10">
          Parece familiar?
        </p>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          {problems.map(({ icon, text }) => (
            <div
              key={text}
              className="bg-card border border-border rounded-xl p-5 space-y-3"
            >
              <span className="text-2xl">{icon}</span>
              <p className="text-sm text-foreground leading-relaxed">{text}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

// ─── Como funciona ────────────────────────────────────────────────────────────

const steps = [
  {
    number: "01",
    title: "Crie sua conta",
    description: "Cadastro em menos de 1 minuto. Sem formulário longo, sem burocracia.",
  },
  {
    number: "02",
    title: "Cadastre seus produtos",
    description: "Adicione o catálogo com preços, estoque e código de barras.",
  },
  {
    number: "03",
    title: "Abra o caixa e venda",
    description: "PDV pronto para uso. Estoque atualiza automaticamente a cada venda.",
  },
];

function HowItWorksSection() {
  return (
    <section className="bg-background py-20 px-6">
      <div className="max-w-4xl mx-auto">
        <div className="text-center mb-12 space-y-2">
          <h2 className="text-2xl font-semibold text-foreground">Como funciona</h2>
          <p className="text-sm text-muted-foreground">Três passos para sair do caos e entrar no controle.</p>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-6">
          {steps.map(({ number, title, description }) => (
            <div key={number} className="space-y-3">
              <span className="text-4xl font-bold text-primary/20 leading-none">{number}</span>
              <h3 className="font-semibold text-foreground">{title}</h3>
              <p className="text-sm text-muted-foreground leading-relaxed">{description}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

// ─── O que está incluso ───────────────────────────────────────────────────────

const features = [
  { icon: ShoppingCart,  label: "PDV com leitor de código de barras" },
  { icon: Package,       label: "Controle de estoque em tempo real" },
  { icon: Landmark,      label: "Gestão de caixa e movimentações" },
  { icon: ShoppingBag,   label: "Módulo de compras e fornecedores" },
  { icon: BarChart3,     label: "Relatórios e histórico de vendas" },
  { icon: Store,         label: "Suporte a múltiplas lojas" },
  { icon: Users,         label: "Usuários com permissões por função" },
  { icon: TrendingUp,    label: "Dashboard com visão geral do negócio" },
];

function FeaturesSection() {
  return (
    <section className="bg-muted/30 py-20 px-6 border-y border-border">
      <div className="max-w-4xl mx-auto">
        <div className="text-center mb-12 space-y-2">
          <h2 className="text-2xl font-semibold text-foreground">Tudo em um só lugar</h2>
          <p className="text-sm text-muted-foreground">Sem módulos pagos separados. Sem surpresa no boleto.</p>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          {features.map(({ icon: Icon, label }) => (
            <div
              key={label}
              className="flex items-center gap-3 bg-card border border-border rounded-lg px-4 py-3"
            >
              <CheckCircle2 className="h-4 w-4 text-primary shrink-0" />
              <span className="text-sm text-foreground">{label}</span>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

// ─── Beta ─────────────────────────────────────────────────────────────────────

function BetaSection() {
  return (
    <section className="bg-background py-20 px-6">
      <div className="max-w-2xl mx-auto text-center space-y-4">
        <Badge
          variant="outline"
          className="border-primary/30 text-primary bg-primary/5 text-xs font-normal px-3 py-1"
        >
          Fase beta
        </Badge>
        <h2 className="text-2xl font-semibold text-foreground">
          Os primeiros lojistas estão entrando agora
        </h2>
        <p className="text-sm text-muted-foreground leading-relaxed max-w-md mx-auto">
          O Nexo está em fase de ativação com os primeiros usuários reais.
          Seu feedback molda diretamente o produto. É isso — sem promessa exagerada.
        </p>
      </div>
    </section>
  );
}

// ─── CTA final ────────────────────────────────────────────────────────────────

function CtaFinalSection() {
  return (
    <section className="bg-[hsl(222,47%,11%)] py-24 px-6">
      <div className="max-w-xl mx-auto text-center space-y-6">
        <h2 className="text-3xl font-bold text-white leading-tight">
          Chega de improvisar.
          <br />
          <span className="text-[hsl(217,91%,60%)]">Comece grátis hoje.</span>
        </h2>
        <p className="text-white/50 text-sm">
          Sem cartão de crédito. Sem limite de vendas no beta.
        </p>
        <Button
          asChild
          size="lg"
          className="bg-[hsl(217,91%,60%)] hover:bg-[hsl(217,91%,55%)] text-white border-0 px-10 text-base font-medium"
        >
          <Link to="/register">
            Criar conta grátis
            <ArrowRight className="ml-2 h-4 w-4" />
          </Link>
        </Button>
      </div>
    </section>
  );
}

// ─── Footer ───────────────────────────────────────────────────────────────────

function LandingFooter() {
  return (
    <footer className="bg-[hsl(222,47%,8%)] py-8 px-6 border-t border-white/5">
      <div className="max-w-6xl mx-auto flex flex-col sm:flex-row items-center justify-between gap-4">
        <span className="text-white/40 text-sm font-medium">
          Nexo<span className="text-[hsl(217,91%,60%)]">ERP</span>
          <span className="ml-2 font-normal">— Gestão inteligente para empresas reais.</span>
        </span>
        <nav className="flex items-center gap-5 text-sm text-white/40">
          <Link to="/login" className="hover:text-white/70 transition-colors">
            Entrar
          </Link>
          <Link to="/register" className="hover:text-white/70 transition-colors">
            Criar conta
          </Link>
        </nav>
      </div>
    </footer>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function LandingPage() {
  return (
    <div className="min-h-screen flex flex-col">
      <LandingNav />
      <main className="flex-1">
        <HeroSection />
        <ProblemSection />
        <HowItWorksSection />
        <FeaturesSection />
        <BetaSection />
        <CtaFinalSection />
      </main>
      <LandingFooter />
    </div>
  );
}
