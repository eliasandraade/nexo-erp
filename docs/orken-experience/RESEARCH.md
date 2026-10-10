# Pesquisa e diagnóstico, 9 de outubro de 2026

## Estado anterior
Base: origin/master 8f5c6ab. React 18, Vite 5, TypeScript, Tailwind 3, Radix/shadcn, React Query, React Hook Form/Zod. 438 arquivos em src. Rotas lazy por módulos. AuthProvider valida /auth/me e limpa cache ao sair/trocar loja. Guards separados por sessão, papel, módulo e capabilities de Service. Preservar estes contratos.

Shells distintos: MainApp, Auth, Platform, Pos, Waiter e Kitchen. Core: clientes, produtos, estoque, vendas, fornecedores, caixa, usuários, auditoria, perfil, assinatura e configurações. Verticais: Service, restaurante e obras. Públicos: landing, autenticação, cardápio/rastreamento e agendamento.

Problemas observados no código: navy/indigo fixos em auth e workspaces; Inter/Syne remotas; login com eventos financeiros inventados e status “Sistema operando” sem fonte; recuperação aponta a rota inexistente; menu mobile sem diálogo/foco; Platform sem adaptação mobile; sidebar usa links que recarregam aplicativo; filtros de cliente locais sobre página do servidor não explicam escopo; dashboard transforma falha/ausência em zeros e estoque saudável; muitos títulos pequenos, barras decorativas, números em fonte display e copy redundante. Playwright importa pacote inexistente. ThemeMood do portal não aceita “classic” usado na configuração.

## Pesquisa primária
Fontes consultadas ao vivo, sem presumir que todas as decisões desses produtos são adequadas ao ORKEN.

| Fonte | Observação e aplicação |
| --- | --- |
| [Linear, changelog](https://linear.app/changelog) | Atualização de 8/10/2026 reúne informações relacionadas e expõe estados de revisão no contexto. Aplicação: contexto e ações próximos; não copiar sua composição ou aparência. |
| [Attio, 2026](https://attio.com/changelog/2026) | Evolução de páginas de registros, permissões e experiência mobile. Aplicação: registros e tarefas têm prioridade sobre widgets; autonomia de IA não é motivo para acrescentar chatbot. |
| [Shopify, Table](https://shopify.dev/docs/api/app-home/latest/web-components/layout-and-structure/table) | Tabela com paginação/filtros e apresentação de lista no mobile. Aplicação: clientes legíveis no celular, preservando dados e acesso ao registro. Não migrar stack para web components. |
| [Ramp, aprovações](https://support.ramp.com/bill-pay-approvals/) | Contexto e recomendações dependem da participação na cadeia de aprovação. Aplicação: exceções visíveis e ações limitadas à autorização existente. |
| [Square, profissionais](https://squareup.com/help/us/en/article/5350-create-staff-member-profiles-for-square-appointments) | Acesso a agenda e clientes vinculado às permissões do profissional. Aplicação: mesma base visual com modos operacionais diferentes. |
| [Mercury, Designing in the open](https://mercury.com/blog/designing-in-the-open) | Interface demonstrável permite avaliar detalhes e fluxos. Aplicação: navegador executável e evidência do teste, sem imagens inventadas como prova de produto. |
| [Digital.gov, tipografia](https://digital.gov/resources/an-introduction-to-typography) | Legibilidade, distinção de caracteres e contexto importam mais que a família por si só. Public Sans tem fonte aberta, mas não é mais mantida segundo a página. Aplicação: família nativa com escala revisada. |

## Síntese crítica
Inferência de projeto, não medição de mercado: padrões duráveis incluem registros densos, filtros explícitos, navegação contextual, busca por comando e estados de erro recuperáveis. Consideramos saturados os recursos visuais rejeitados no briefing, não por uma alegação estatística sobre todo o mercado. IA pode ser uma ação situada, com revisão e limites. Configurações continuam agrupadas por objeto e responsabilidade. Responsividade precisa reorganizar leitura e ações.

## Arquitetura proposta
Empresa/loja → área de trabalho → operação/cadastros/administração → registro. Sidebar contextual permanece porque há destinos frequentes e comparáveis; deixa de ser uma faixa escura dominante. Comando reutiliza exatamente os destinos permitidos. A troca de área não equivale à troca de tenant. Não prometer busca de entidades sem endpoint compatível.

## Piloto e expansão
Clientes: tabela, filtros, vazio, erro, paginação e edição existentes. Refinar antes de expandir foundation para shell, dashboard, formulários, Service, varejo, restaurante, Platform e superfícies públicas. Caixa/PDV: apenas apresentação e responsividade; manter estados de transação, scanner, confirmação, estoque e pagamentos.

## Integridade
Sem alteração de backend/API, cobrança, DNS, secrets ou produção. Branch própria a partir da master remota evita incluir os três commits de comissões em andamento no checkout original. Nenhum merge automático.
