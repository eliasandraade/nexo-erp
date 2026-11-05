# Tax Intelligence — checklist de release

Uma checklist por release que toca o módulo. Marque cada item com evidência (link do CI, print, id de auditoria).
Itens **[PO]** só o Product Owner decide; itens **[PROF]** exigem revisão profissional real — nunca simulada.

## A. Código (antes do merge)

- [ ] CI verde: `backend-tests` (unit + integração), `frontend-ci` (typecheck, lint, vitest, build), Playwright.
- [ ] SonarCloud: Quality Gate PASS; cobertura de código novo ≥ 90%; zero bug/vulnerabilidade nova; hotspots
      novos revisados. O gate não foi alterado para passar e nenhum código foi excluído da análise para esconder problema.
- [ ] GitGuardian sem segredo.
- [ ] `dotnet list package --vulnerable --include-transitive` vazio; `npm audit --omit=dev` sem high/critical
      (moderadas triadas em `docs/security/`).
- [ ] `dotnet ef migrations has-pending-model-changes` → nenhuma; migrações novas aditivas e com defaults que não
      quebram linhas existentes.
- [ ] Revisão de código independente (Codex) sem finding aberto.
- [ ] Bateria `TaxProfessionalFinalTests` verde — aprovações profissionais **fictícias, só no banco de teste**.
- [ ] Jornadas A–J (`TaxJourneyTests`) verdes.
- [ ] Nenhum texto chama redução de IBS/CBS calculado de "economia"; nenhum nome de pessoa ou registro profissional
      no código ou em seed de produção.

## B. Deploy (módulo continua desligado)

- [ ] Deploy com `TaxIntelligence__Policy__Enabled` ausente/`false` e `Mode` ausente/`Disabled`.
- [ ] `/health` 200; ERP sem regressão (login, PDV, caixa).
- [ ] `POST /api/tax/calculations` → 422 `tax_intelligence_disabled` (com sessão de empresa).
- [ ] `GET /api/platform/tax/module` → `status: Inactive`, `newWorkRuns: false`, `configurationProblems: []`.
- [ ] Rotas novas sem token → 401; com sessão de empresa em rota de plataforma → 403.
- [ ] Bucket R2 privado: versionamento ligado, sem lifecycle de expiração em `private/`.
- [ ] Backup manual do schema `nexo` feito antes das migrações (fora do repositório).

## C. Piloto controlado — só com decisão **[PO]**

- [ ] **[PO]** Decisão escrita com referência (vai em cada registro).
- [ ] Fontes conferidas com o texto oficial (quem comparou + quem registrou).
- [ ] Versões de regra ativadas; aprovação interna registrada por regra com escopo, vigência e ressalvas.
- [ ] Catálogo cClassTrib importado de fonte documentada e ativado; obrigações acessórias cadastradas;
      padrões de retenção definidos.
- [ ] Escopo do piloto: janela, referência, empresas/lojas.
- [ ] Prontidão sem `Blocking`.
- [ ] Deploy com `Enabled=true`, `Mode=ControlledPilot`.
- [ ] Assistente de ativação concluído (frase digitada, modo sombra reconhecido) — anotar o id do evento
      `tax_module_activated`.
- [ ] Smoke com uma empresa do piloto; resultado com `governance.mode = ControlledPilot` e aviso de efeitos.
- [ ] Ensaio do freio: suspender e reativar em janela combinada, conferindo `tax_module_suspended` na auditoria.

## D. Modo definitivo — só com **[PO]** e **[PROF]**

- [ ] **[PROF]** Para cada regra exigida, revisão profissional **recebida de fato**, registrada com o profissional
      como informado, escopo, regimes cobertos, vigência, premissas/ressalvas e documento de suporte.
- [ ] **[PROF]** Códigos cClassTrib usados com revisão profissional aprovada.
- [ ] Assistente do modo definitivo sem regras pendentes.
- [ ] **[PO]** Decisão escrita para trocar o modo.

## E. Depois do release

- [ ] Métricas `orken.tax.refusals` e `orken.tax.coverage_refusals` acompanhadas na primeira semana.
- [ ] Recusas recentes revisadas (`/platform/tributos?aba=auditoria`).
- [ ] Reprodução (`/verify`) de uma amostra de cálculos do período.
