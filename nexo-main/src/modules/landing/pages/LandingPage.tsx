import { useState } from "react";
import { Link } from "react-router-dom";
import { Brand } from "@/components/shared/Brand";
import { Button } from "@/components/ui/button";

const operations = [
  { name: "Varejo", task: "Da venda ao fechamento.", steps: ["Produto", "Venda", "Caixa", "Estoque"], detail: "Catálogo, frente de caixa e movimentações de estoque. Consulte vendas e cadastros no mesmo ambiente." },
  { name: "Serviços", task: "Do horário ao recebimento.", steps: ["Cliente", "Agenda", "Ordem", "Pagamento"], detail: "Organize profissionais, catálogo, agendamentos, ordens e pacotes conforme a operação da sua empresa." },
  { name: "Restaurante", task: "Da mesa à cozinha.", steps: ["Mesa", "Comanda", "Cozinha", "Conta"], detail: "Acompanhe mesas, comandas, preparo e entregas, com uma tela apropriada para cada equipe." },
  { name: "Obras", task: "Cada despesa no seu contexto.", steps: ["Obra", "Etapa", "Despesa", "Registro"], detail: "Mantenha despesas e registros associados à obra e à etapa em que aconteceram." },
];
export default function LandingPage() {
  const [selected, setSelected] = useState(0);
  const operation = operations[selected];
  return <div className="min-h-dvh bg-background">
    <a href="#landing-main" className="skip-link">Pular para o conteúdo</a>
    <header className="mx-auto flex max-w-7xl items-center justify-between gap-4 px-6 py-7 sm:px-10">
      <Link to="/" aria-label="ORKEN, início"><Brand /></Link>
      <nav aria-label="Navegação do site" className="flex items-center gap-6 text-sm"><a href="#operacoes" className="hidden hover:underline sm:block">Operações</a><a href="#acesso" className="hidden hover:underline sm:block">Como acessar</a><Button asChild variant="outline"><Link to="/login">Entrar no ORKEN</Link></Button></nav>
    </header>
    <main id="landing-main">
      <section id="operacoes" className="mx-auto grid max-w-7xl gap-12 px-6 pb-16 pt-10 sm:px-10 lg:grid-cols-[.8fr_1.2fr] lg:gap-20 lg:py-24">
        <div>
          <p className="mb-6 text-sm font-medium text-primary">Software para o trabalho de todo dia</p>
          <h1 className="max-w-lg text-4xl font-semibold leading-[1.1] tracking-tight sm:text-5xl">Cada operação tem seu jeito.<br /><span className="text-primary">E seu lugar no ORKEN.</span></h1>
          <p className="mt-7 max-w-md text-base leading-relaxed text-muted-foreground">No balcão, na agenda, no salão ou na obra. Registre o que acontece e acompanhe o que precisa da sua atenção.</p>
          <Button asChild className="mt-8"><Link to="/register">Criar meu acesso →</Link></Button>
        </div>
        <div className="min-w-0 self-center">
          <div role="tablist" aria-label="Tipos de operação" className="flex flex-wrap gap-1 border-b border-border pb-3">
            {operations.map((item, index) => <button key={item.name} id={'operation-tab-' + index} role="tab" aria-selected={selected === index} aria-controls="operation-panel" tabIndex={selected === index ? 0 : -1} onClick={() => setSelected(index)} onKeyDown={event => { if (["ArrowRight", "ArrowLeft", "Home", "End"].includes(event.key)) { event.preventDefault(); const next = event.key === "Home" ? 0 : event.key === "End" ? operations.length - 1 : (index + (event.key === "ArrowRight" ? 1 : -1) + operations.length) % operations.length; setSelected(next); document.getElementById('operation-tab-' + next)?.focus(); } }} className={'min-h-11 rounded px-4 text-sm font-medium ' + (selected === index ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-muted')}>{item.name}</button>)}
          </div>
          <div id="operation-panel" role="tabpanel" aria-labelledby={'operation-tab-' + selected} className="pt-8">
            <h2 className="text-2xl font-semibold tracking-tight">{operation.task}</h2>
            <ol aria-label="Etapas da operação" className="my-8 grid grid-cols-2 border-y border-border sm:grid-cols-4">
              {operation.steps.map((step, index) => <li key={step} className="border-r border-border px-3 py-8 last:border-r-0"><span aria-hidden className="mb-6 block text-xs text-muted-foreground">{String(index + 1).padStart(2, '0')}</span><span className="text-base font-semibold">{step}</span></li>)}
            </ol>
            <p className="min-h-20 max-w-prose text-sm leading-relaxed text-muted-foreground">{operation.detail}</p>
            <p className="mt-4 text-xs text-muted-foreground">Os recursos disponíveis dependem dos módulos e das permissões da empresa.</p>
          </div>
        </div>
      </section>
      <section className="bg-primary text-primary-foreground">
        <div className="mx-auto grid max-w-7xl gap-10 px-6 py-16 sm:px-10 md:grid-cols-2 md:gap-20">
          <h2 className="max-w-md text-3xl font-semibold leading-tight tracking-tight">A mesma empresa.<br />Diferentes formas de trabalhar.</h2>
          <div className="space-y-6 text-base leading-relaxed"><p>Quem atende precisa da próxima tarefa. Quem gerencia precisa entender a operação. Quem administra precisa controlar os acessos.</p><p>O ORKEN organiza essas áreas por módulo, loja e perfil de usuário, preservando o contexto de cada equipe.</p></div>
        </div>
      </section>
      <section id="acesso" className="mx-auto grid max-w-7xl gap-10 px-6 py-16 sm:px-10 md:grid-cols-2 md:gap-20">
        <div><h2 className="text-3xl font-semibold tracking-tight">Antes de começar</h2><p className="mt-4 max-w-md text-sm leading-relaxed text-muted-foreground">O ORKEN funciona no navegador e precisa de conexão com a internet. Uma conta permite acessar as áreas liberadas para sua empresa.</p></div>
        <div className="divide-y divide-border border-y border-border">
          <details className="py-5"><summary className="cursor-pointer font-medium">Minha empresa pode usar mais de uma área?</summary><p className="mt-4 text-sm leading-relaxed text-muted-foreground">Sim. As áreas disponíveis acompanham os módulos ativos na assinatura. A troca de área fica no menu do produto.</p></details>
          <details className="py-5"><summary className="cursor-pointer font-medium">Cada pessoa vê os mesmos recursos?</summary><p className="mt-4 text-sm leading-relaxed text-muted-foreground">O acesso depende do perfil, dos módulos e das permissões atribuídas pela empresa.</p></details>
          <details className="py-5"><summary className="cursor-pointer font-medium">Como entro em uma empresa existente?</summary><p className="mt-4 text-sm leading-relaxed text-muted-foreground">Peça seu acesso ao administrador. Se você já tem login e senha, entre no ORKEN.</p></details>
        </div>
      </section>
    </main>
    <footer className="border-t border-border"><div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-6 px-6 py-8 sm:px-10"><Brand /><span className="text-xs text-muted-foreground">Andrade Systems</span><a href="mailto:suporte@orken.com.br" className="text-sm text-primary hover:underline">Falar com o suporte</a></div></footer>
  </div>;
}
