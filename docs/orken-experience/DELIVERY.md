# ORKEN — entrega para revisão humana

Branch: codex/orken-experience. Base inicial: origin/master, 8f5c6ab; atualizada com master 6788fed antes da PR. Rodadas de trabalho em 9–10 de outubro de 2026. Sem merge em master ou deploy. O checkout original não foi alterado. As correções de comissões já aprovadas na master foram incorporadas à branch de revisão.

## 1. Diagnóstico e conceito

O frontend combinava shells incompatíveis, navy/indigo fixos, tipografia remota, painéis repetidos e mensagens sem fonte real. O login exibia atividade financeira fictícia; a plataforma não tinha navegação móvel adequada; a lista de clientes confundia falha com vazio e ocultava o escopo local de filtros.

A direção é **ORKEN: sistema de registros**. Navegação discreta, contexto de empresa/loja, títulos legíveis e ações próximas ao trabalho. Verde profundo, superfícies claras e números tabulares. Cozinha mantém contexto escuro; portais respeitam a marca configurada pelo estabelecimento.

Pesquisa primária e diagnóstico detalhado: [RESEARCH.md](RESEARCH.md). Direção de produto: [PRODUCT.md](../../PRODUCT.md). Princípios, tokens, componentes, conteúdo e motion: [DESIGN.md](../../DESIGN.md).

## 2. Arquitetura de informação e navegação

- Shell principal: área ativa → grupo operacional → página. Empresa/loja no cabeçalho, perfil no menu, conteúdo com link de salto.
- Ctrl/Cmd+K pesquisa destinos autorizados pela mesma composição de navegação. Não simula pesquisa de entidades.
- Celular: menu em diálogo com foco contido, Escape e restauração de foco. Plataforma usa a mesma mecânica com grupos próprios.
- Áreas: Varejo, Serviços, Restaurante e Obras; seleção preserva a lógica de módulos habilitados e o destino de cada workspace.
- PDV e cozinha continuam ambientes operacionais próprios, com suas regras e estados transacionais.
- Rotas, guards, permissões e endpoints de servidor não foram reescritos. Nenhuma mudança própria de backend, banco, infraestrutura, secrets ou produção no diff contra a master atual.

## 3. Componentes e tokens

A fundação está em nexo-main/src/index.css e tailwind.config.ts. Tokens semânticos HSL preservam compatibilidade com Tailwind/Radix; nenhuma biblioteca visual nova. Fonte Segoe UI Variable/Segoe UI/system, sem solicitação de fonte remota. Escala de espaço de 4px; texto operacional de 14px; títulos de página de 28px; contornos de 1px; radius de 4–6px; foco visível; redução de movimento; controles com altura mínima de 44px em ponteiro de toque.

Componentes comuns: Brand, PageHeader, SectionCard, MetricBlock, TableShell, Table, StatusBadge, EmptyState, ErrorState, PageSkeleton, ErrorFallback, CommandMenu e navegação. Tabs passam a rolar dentro da própria região em telas estreitas. Cabeçalhos de formulários empilham título e ações no celular.

## 4. Cobertura da reconstrução

| Área | Mudança |
| --- | --- |
| Clientes, piloto | Tabela desktop e lista móvel com rótulos; contatos agrupados; erro distinto de vazio; filtros identificados e com escopo explícito; paginação mantida; campos do formulário associados a rótulos. |
| Dashboard | Prioridades de caixa, estoque e cancelamentos; faixa de indicadores; gráfico de dados reais com período explícito; falha não vira receita zero/estoque saudável. |
| Login, cadastro, verificação e reenvio | Composição clara, nova marca, remoção de atividade inventada, foco no erro, senha acessível por teclado, erro de reenvio comunicado. Link de recuperação inexistente substituído por orientação ao administrador. |
| Workspaces | Lista de áreas reais e acesso por módulo, sem cenografia decorativa. |
| Service | Visão geral em métricas e tarefas; onboarding compacto; fundação comum em agenda, ordens, pacotes, pagamentos, profissionais, catálogo e portal. Plural de profissional corrigido. |
| Varejo, estoque e cadastros | Shell, títulos, controles, tabelas, superfícies e densidade comuns em produtos, fornecedores, usuários, auditoria, vendas, caixa, perfil, assinatura e configurações. Busca de produto não conserva contraste do antigo tema escuro. |
| PDV | Nova barra de contexto, composição vertical no celular e lateral no desktop; erro/carregamento de caixa distintos de caixa fechado; handlers de venda/pagamento preservados. |
| Restaurante e Obras | Superfícies e tipografia comuns, breadcrumbs legíveis, cozinha escura preservada com contraste próprio; formulários e relatórios herdam a fundação. Fluxos de comandas, entregas e obra preservados. |
| Plataforma | Navegação global agrupada, menu móvel, contexto explícito; indisponibilidade de estatísticas/saúde não representada como zero ou espera infinita. |
| Landing | Composição original com quatro operações e diagramas de processo, tabs por teclado, FAQ factual e CTAs existentes. Removidos 13 componentes antigos sem consumidores. |
| Portal de agendamento | Cabeçalho usa identidade real da loja; menos movimento/sombra; fonte nativa; ícones sem associação aleatória; texto de botão escolhe preto/branco conforme luminância da cor configurada. |
| Cardápio público/rastreamento | Fundação e controles harmonizados, retirada de gradiente padrão; conteúdo, imagens e regras do estabelecimento preservados. |
| Falhas de aplicação | ErrorFallback agora segue a marca e oferece recuperação sem exibir detalhes internos. |

Nem toda página precisou de uma composição nova: telas operacionais com semântica adequada foram alinhadas pelos componentes e tokens compartilhados. A matriz acima diferencia reconstrução estrutural de harmonização visual.

## 5. Decisões técnicas e dependências

Nenhuma dependência adicionada ou removida. React, Vite, Tailwind, Radix, React Query e bibliotecas existentes mantidos. O pacote inexistente lovable-agent-playwright-config deixou de ser importado pela configuração e fixture de Playwright; usa-se @playwright/test já presente.

TypeScript passou a strict/noImplicitAny. Corrigidas incompatibilidades anteriores em PageSkeleton, uniões discriminadas de auth/cancelamento, tipo de rótulo de venda, query keys de Service, ThemeMood, fixture de marca, import UserCog e teste de promessa. Ajustes são de tipagem: formatos de chamadas e contratos do backend preservados. A correção posterior da master em UpdateArea foi preservada: editar/ativar uma área mantém sua descrição, em vez de apagá-la.

Vitest exclui arquivos .e2e.spec.ts e limita dois workers. O teste de JWT usa um payload sintético estruturalmente válido. Playwright tem configuração local autônoma, usando Edge no Windows. A suíte de autenticação da master foi preservada em e2e/auth.e2e.spec.ts e usa playwright.auth.config.ts via npm run test:e2e; exige backend real e não foi executada nesta revisão. npm run test:ui executa somente a suíte visual com fixtures. Chromium instalado retornou spawn UNKNOWN neste ambiente; a evidência válida foi produzida com Edge.

Build gerado fora do dist versionado, em node_modules/.orken-build. Nenhum bundle gerado integra esta entrega.

## 6. Validação executada

- Typecheck estrito: aprovado.
- ESLint: zero erros; 16 avisos existentes de fast refresh/hooks permanecem registrados.
- Vitest: 18 arquivos, 179 testes aprovados.
- Playwright: 29 testes aprovados; cenários em e2e/experience.spec.ts, incluindo oito larguras (1920, 1440, 1366, 1280, 1024, 768, 390 e 320), lista de 25 registros com textos longos, vazio, falha, filtros, teclado, retorno de foco, restrição de papel, movimento reduzido, formulários, plataforma, PDV e portal público. Resultado final em browser-tests.txt.
- Build Vite: aprovado; avisos de anotação PURE da dependência SignalR registrados, sem erro de build.
- Revisão visual de screenshots: login, landing, clientes desktop/mobile, formulário de cliente, plataforma, PDV e portal. Não constitui certificação WCAG ou validação em dispositivos físicos.

Logs atuais: types-current.txt, lint-current.txt, tests-current.txt, browser-tests.txt e build.txt. Logs baseline preservam problemas anteriores. O lint baseline também capturou um script temporário durante a execução; não é uma contagem pura do estado original. Os scripts temporários de migração foram removidos.

## 7. Comparações visuais

Sessão **Ambiente de teste** e registros sintéticos usados somente no harness. Nenhum dado de cliente foi publicado. Antes obtido do código original; fontes remotas bloqueadas no harness, portanto a comparação anterior usa fallback local. Dimensões iguais em cada par.

| Tela | Antes | Depois |
| --- | --- | --- |
| Login 1440 | [imagem](screenshots/before/login-1440.png) | [imagem](screenshots/after/login-1440.png) |
| Landing 1440 | [imagem](screenshots/before/landing-1440.png) | [imagem](screenshots/after/landing-1440.png) |
| Clientes 1440 | [imagem](screenshots/before/clientes-1440.png) | [imagem](screenshots/after/clientes-1440.png) |
| Clientes 390 | [imagem](screenshots/before/clientes-390.png) | [imagem](screenshots/after/clientes-390.png) |

Evidências adicionais: [formulário móvel](screenshots/qa/-clientes-novo-390.png), [plataforma](screenshots/qa/platform-390.png), [PDV](screenshots/qa/pdv-390.png), [agendamento](screenshots/qa/portal-390.png). As capturas de clientes com 25 registros são longas de propósito para documentar toda a paginação.

## 8. Reprodução

No diretório nexo-main, instalar dependências e executar:

~~~text
npm run typecheck
npm run lint
npm test
npm run build -- --outDir node_modules/.orken-build
npm run test:ui
~~~

Playwright inicia/reutiliza Vite em 127.0.0.1:5179. Windows requer Edge; outros sistemas usam Chromium do Playwright. O backend é interceptado com fixtures locais. O script scripts/capture-experience.mjs captura comparações e aceita REVIEW_URL para um servidor com a versão original.

## 9. QA humana exigida antes de merge

A validação automatizada é de frontend com respostas controladas. Falta validar em ambiente de homologação real: login/renovação/convite, cada papel e módulo, criação/edição de registros, transações completas de PDV/caixa, scanner/impressão, comanda/cozinha/SignalR, pagamento/estorno e reserva pública concorrente. Verificar Safari/iOS/Android físicos, leitor de tela falado, zoom 200%, contraste das marcas de clientes e uso prolongado com dados reais volumosos.

Filtros de tipo/situação de clientes continuam limitados à página atual pelo contrato existente; a interface agora informa isso. Pesquisa de comandos navega apenas páginas. Não foi criada busca global de dados ou recuperação de senha no backend.

A aprovação visual e funcional final é humana. A PR deve permanecer Draft, sem merge em master ou deploy.

Integração da master: durante a execução a base avançou 21 commits. Foi feito merge da master para esta branch, sem reescrever histórico. Os conflitos de tipagem equivalentes foram conciliados; as alterações de segurança/comissões da base e a preservação de descrição das áreas continuam intactas. Os testes e o build foram repetidos depois dessa integração.

## 10. Commits e PR

- 2c21c45 — diagnóstico, pesquisa e direção.
- 8a6bdef — fundação, shell e piloto de clientes.
- a4a0663 — expansão para operações e superfícies públicas.
- a212050 — configuração de QA, 29 cenários de navegador e tipagem estrita.
- O último commit registra o relatório e as evidências. Consultar a PR vinculada para o histórico completo.
