import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
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
  Layers,
  Shield,
  Zap,
  RefreshCw,
} from "lucide-react";

// ─── Navbar ──────────────────────────────────────────────────────────────────

function LandingNav() {
  return (
    <header className="fixed top-0 left-0 right-0 z-50 bg-[hsl(222,47%,11%)]/95 backdrop-blur border-b border-white/5">
      <div className="max-w-6xl mx-auto px-6 h-14 flex items-center justify-between">
        <img
          src="/orken_darkmode.png"
          alt="Orken"
          className="h-7 w-auto object-contain"
        />
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
        <h1 className="text-4xl sm:text-5xl font-bold text-white leading-tight tracking-tight">
          Venda, controle o estoque
          <br />
          <span className="text-[hsl(217,91%,60%)]">e feche o caixa — no mesmo lugar.</span>
        </h1>

        <p className="text-lg text-white/60 max-w-xl mx-auto leading-relaxed">
          Cada venda registrada no Orken atualiza o estoque e o caixa
          automaticamente. Sem planilha. Sem digitação dupla. Sem perder o controle.
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
            Sem cartão de crédito · Configuração em minutos.
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
        <p className="text-center text-sm text-muted-foreground mt-8">
          O Orken foi feito para resolver exatamente esses três problemas — de uma vez.
        </p>
      </div>
    </section>
  );
}

// ─── Modularidade ─────────────────────────────────────────────────────────────

const modules = [
  {
    icon: ShoppingCart,
    title: "Venda mais rápido",
    description:
      "PDV com leitor de código de barras. Estoque e caixa atualizados a cada venda, automaticamente.",
  },
  {
    icon: ShoppingBag,
    title: "Compre melhor",
    description:
      "Controle pedidos a fornecedores, recebimentos e o custo real de cada produto.",
  },
  {
    icon: Landmark,
    title: "Saiba se está lucrando",
    description:
      "Fluxo de caixa, contas a pagar e resultado real do mês — sem precisar de planilha para entender.",
  },
  {
    icon: Store,
    title: "Cresça sem perder controle",
    description:
      "2, 3 ou 10 lojas no mesmo painel. Cada operação separada, tudo visível de um lugar só.",
  },
];

function ModularitySection() {
  return (
    <section className="bg-background py-20 px-6">
      <div className="max-w-4xl mx-auto">
        <div className="text-center mb-12 space-y-3">
          <div className="inline-flex items-center gap-2 text-primary">
            <Layers className="h-5 w-5" />
            <span className="text-sm font-medium uppercase tracking-widest">Arquitetura modular</span>
          </div>
          <h2 className="text-2xl font-semibold text-foreground">
            Comece pelo que você precisa.
            <br />
            Expanda conforme crescer.
          </h2>
          <p className="text-sm text-muted-foreground max-w-lg mx-auto leading-relaxed">
            Cada módulo funciona de forma independente e se integra aos outros.
            Ideal para quem tem mais de um negócio em ramos diferentes ou
            uma rede com várias filiais.
          </p>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          {modules.map(({ icon: Icon, title, description }) => (
            <div
              key={title}
              className="flex items-start gap-4 bg-card border border-border rounded-xl p-5"
            >
              <div className="w-10 h-10 rounded-lg bg-primary/8 flex items-center justify-center shrink-0">
                <Icon className="h-5 w-5 text-primary" />
              </div>
              <div>
                <p className="font-semibold text-foreground text-sm">{title}</p>
                <p className="text-sm text-muted-foreground mt-0.5 leading-relaxed">{description}</p>
              </div>
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
    description: "Cadastro em menos de 1 minuto. Sem formulário longo.",
  },
  {
    number: "02",
    title: "Configure sua operação",
    description: "Produtos, estoque, lojas e usuários. Tudo no painel.",
  },
  {
    number: "03",
    title: "Abra o caixa e opere",
    description: "PDV pronto para uso. Estoque e caixa atualizados em tempo real.",
  },
];

function HowItWorksSection() {
  return (
    <section className="bg-muted/30 py-20 px-6 border-y border-border">
      <div className="max-w-4xl mx-auto">
        <div className="text-center mb-12 space-y-2">
          <h2 className="text-2xl font-semibold text-foreground">Como funciona</h2>
          <p className="text-sm text-muted-foreground">Do cadastro à operação em três passos.</p>
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
  { icon: Store,         label: "Multi-loja e multi-operação" },
  { icon: Users,         label: "Usuários com permissões por função" },
  { icon: TrendingUp,    label: "Dashboard com visão geral do negócio" },
];

function FeaturesSection() {
  return (
    <section className="bg-background py-20 px-6">
      <div className="max-w-4xl mx-auto">
        <div className="text-center mb-12 space-y-2">
          <h2 className="text-2xl font-semibold text-foreground">Tudo integrado</h2>
          <p className="text-sm text-muted-foreground">Uma plataforma para centralizar toda a operação.</p>
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

// ─── Confiança técnica ────────────────────────────────────────────────────────

const trustItems = [
  {
    icon: RefreshCw,
    title: "Atualizações sem downtime",
    description: "O sistema é atualizado sem interromper sua operação. Você nunca perde uma venda por manutenção.",
  },
  {
    icon: Shield,
    title: "Multi-tenant seguro",
    description: "Os dados de cada empresa são completamente isolados. Sua loja é sua — ninguém mais acessa.",
  },
  {
    icon: Zap,
    title: "Infraestrutura de alta demanda",
    description: "Construído sobre a mesma base tecnológica usada em sistemas de gestão de grande volume.",
  },
];

function TrustSection() {
  return (
    <section className="bg-muted/40 py-16 px-6 border-y border-border">
      <div className="max-w-4xl mx-auto">
        <div className="text-center mb-10 space-y-2">
          <h2 className="text-xl font-semibold text-foreground">Infraestrutura para operar de verdade.</h2>
          <p className="text-sm text-muted-foreground max-w-md mx-auto">
            Você opera. A gente cuida do resto.
          </p>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          {trustItems.map(({ icon: Icon, title, description }) => (
            <div key={title} className="flex flex-col gap-3 bg-card border border-border rounded-xl p-5">
              <div className="w-9 h-9 rounded-lg bg-primary/10 flex items-center justify-center">
                <Icon className="h-4 w-4 text-primary" />
              </div>
              <div>
                <p className="text-sm font-semibold text-foreground">{title}</p>
                <p className="text-xs text-muted-foreground mt-1 leading-relaxed">{description}</p>
              </div>
            </div>
          ))}
        </div>
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
          Sua loja no controle.
          <br />
          <span className="text-[hsl(217,91%,60%)]">Hoje mesmo.</span>
        </h2>
        <p className="text-white/50 text-sm">
          Configure em minutos. Comece a vender com PDV real no mesmo dia.
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
        <div className="flex items-center gap-3">
          <img
            src="/orken_darkmode.png"
            alt="Orken"
            className="h-5 w-auto object-contain opacity-60"
          />
          <span className="text-white/40 text-sm font-normal">— Um sistema. Seus negócios.</span>
        </div>
        <nav className="flex items-center gap-5 text-sm text-white/40">
          <Link to="/login" className="hover:text-white/70 transition-colors">
            Entrar
          </Link>
          <Link to="/register" className="hover:text-white/70 transition-colors">
            Criar conta grátis
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
        <ModularitySection />
        <HowItWorksSection />
        <FeaturesSection />
        <TrustSection />
        <CtaFinalSection />
      </main>
      <LandingFooter />
    </div>
  );
}
