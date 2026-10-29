# Arquitetura

## Arquitetura encontrada

O ORKEN é um monólito modular .NET 8 em camadas (Domain → Application → Infrastructure → Api) com React/TS no frontend. Módulos verticais vivem em `Modules/<Nome>` em cada camada (Service, Build, Restaurante, Interpreter). Multi-tenancy por filtro global do EF: `TenantEntity` filtra por `tenant_id`, `StoreEntity` por `tenant_id` **e** `store_id`, com os ids injetados pelo interceptor de gravação. Autorização por conjuntos de papéis (`AppRoles`: Diretoria, Gerente, Vendedor, Estoquista, Cozinha) e uma policy `Platform` para o operador do SaaS. Auditoria em `audit_records` via `IAuditWriter` (gravada na mesma unidade de trabalho). Logs estruturados (Serilog JSON) com `X-Request-Id`. O módulo Fiscal (PR 66) guarda o emissor por loja, mas não calcula tributos.

O Tax Intelligence segue esse desenho; não há arquitetura paralela.

## Camadas

| Camada | Onde | O quê |
|---|---|---|
| Domain | `Nexo.Domain/Modules/TaxIntelligence` | Entidades e invariantes (imutabilidade, transições de estado, saldos do ledger, conciliação do split) |
| Application | `Nexo.Application/Modules/TaxIntelligence` | **Engine pura** (`Engine/`): vocabulário das regras, avaliação, forma canônica e hashes, simulador, detectores, guardrails. DTOs e validadores (FluentValidation) |
| Infrastructure | `Nexo.Infrastructure/TaxIntelligence` | Serviços com EF (`NexoDbContext`), leitura de vendas, catálogo v1 semeado, trilha de auditoria |
| Persistence | `Persistence/Configurations/Modules/TaxIntelligence`, migration `TaxIntelligenceFoundation` | 21 tabelas `tax_*`, só aditivas |
| Api | `Nexo.Api/Controllers/Modules/TaxIntelligence` | `api/tax/*` (tenant) e `api/platform/tax/*` (plataforma) |
| Frontend | `nexo-main/src/modules/tax` | Rotas `/tributos/*` |

A engine não conhece EF, HTTP, relógio nem banco: recebe `TaxEngineInput` + versões de regra e devolve `TaxEngineOutput`. É isso que a torna determinística e testável em unidade.

## Bounded contexts

| Contexto | Entidades | Escopo |
|---|---|---|
| Profiles | `TaxProfile`, `TaxProfileVersion` | estabelecimento (loja) |
| Rules | `TaxRule`, `TaxRuleVersion`, `TaxRuleVersionSource`, `TaxLegalSource` | **plataforma** (sem tenant) |
| Calculations | `TaxTransaction`, `TaxTransactionLine`, `TaxCalculation`, `TaxComponent`, `TaxRuleExecution` | estabelecimento |
| Simulations | `TaxSimulation`, `TaxSimulationScenario` | estabelecimento |
| Opportunities / Decisions / Evidence | `TaxOpportunity`, `TaxOpportunityRequirement`, `TaxOpportunityEvidence`, `TaxDecision` | estabelecimento |
| Credits | `TaxCredit`, `TaxCreditLedgerEntry` | estabelecimento |
| SplitPayment | `SplitExpectation`, `SplitSettlement` | estabelecimento |
| Audit | `audit_records` existente (`TaxAuditTrail`) | — |

**Empresa = tenant; estabelecimento = loja (`Store`).** Dados cadastrais do emissor (razão social, CNPJ, endereço) continuam no `FiscalProfile`; o perfil tributário guarda só os fatos que mudam o cálculo, versionados no tempo. Regime, UF e município aparecem nos dois de propósito: no `FiscalProfile` é o dado atual de emissão; na versão do perfil é o fato histórico usado pelo cálculo. A tela do perfil sugere esses valores a partir do perfil fiscal.

## Fluxo

```
módulo operacional (venda, proposta manual, compra futura)
  └─ fatos → TaxOperationRequest ─────────────┐
perfil vigente na data (TaxProfileVersion) ───┤
                                              ▼
                     TaxEngineInput (canônico, hash)
catálogo: versões que cobrem a data ─► registry.Current.Run ─► TaxEngineOutput (hash)
                                              │
       TaxTransaction + TaxCalculation + TaxComponent + TaxRuleExecution (imutáveis)
                                              │
                 detectores (propõem) ─► TaxOpportunity (Identified)
                                              │ revisão (Management)
                 requisitos + evidências ─► guardrails ─► Blocked?
                                              │ aprovação (Diretoria)
                                   TaxDecision (dossiê congelado)
```

- **Idempotência:** chave = hash(versão da engine | hash do input | hash do conjunto de regras | origem | hash dos fatos). Repetir a mesma operação devolve o cálculo existente (`reused: true`); um índice único no banco garante o mesmo sob concorrência.
- **Vendas:** `SaleTaxFactsReader` lê a venda (só leitura) e declara suas suposições (descontos do PDV tratados como incondicionais, rateio de desconto e acréscimos da venda, itens no regime geral, tributos no preço desconhecidos). Vendas não sabem nada de tributos.
- **Procurement:** `POST /api/tax/simulations/supplier-comparison` calcula o custo efetivo por fornecedor (preço − crédito estimado) em memória. O módulo de compras pode chamá-lo sem acoplamento.

## Vertical slice

**Escolha:** desconto incondicional + IBS/CBS, como pedido. A legislação foi validada antes de codificar:

1. LC 214/2025, art. 12, § 1º, III — desconto concedido sob condição **integra** a base.
2. Art. 12, § 2º, III — descontos incondicionais **não integram** a base; § 3º define desconto incondicional: consta do documento fiscal e não depende de evento posterior.
3. Arts. 343 e 346 — alíquotas de teste de 2026: IBS estadual 0,1%, CBS 0,9%.
4. Art. 348 — em 2026 o recolhido é compensado com PIS/Cofins (I e II); as alíquotas têm redução nos regimes diferenciados e outras bases nos específicos (III, a e b); não se aplicam ao Simples Nacional (III, c); recolhimento dispensado para quem cumpre as obrigações acessórias (§ 1º).

Como o art. 348 torna o efeito de caixa de 2026 nulo ou compensável, o detector **não promete dinheiro**: a oportunidade é do tipo `CalculatedTaxReduction` (natureza `CalculatedTaxReduction` — "redução do IBS/CBS calculado"), **nunca** `TaxSaving`/`DefinitiveSaving`, e não entra em `potentialSavings` no painel (tem o próprio total, `potentialCalculatedTaxReductions`). Se essa redução vira economia em algum período é avaliação de profissional, não do detector; a ressalva de 2026 continua. O detector passou a `unconditional-discount@2` (mesma lógica e cenários; só a natureza mudou); oportunidades `@1` mantêm a chave e o guardrail. O cenário de 2027+ só pode ser simulado com hipótese de alíquota explícita, porque a alíquota de referência depende de resolução do Senado (art. 349).

Fluxo ponta a ponta (coberto por teste de integração):
proposta com desconto condicional → `TaxTransaction` → cálculo base (desconto na base) → detector → cenário alternativo (desconto incondicional) → comparação (−R$ 0,50 em R$ 50 de desconto) → fundamentação (art. 12) → requisitos (consta da nota; não depende de evento posterior; condição comercial retirada) → revisão → aprovação → `TaxDecision`.

## Permissões

O ORKEN usa papéis, não permissões finas; as permissões pedidas foram mapeadas para os conjuntos existentes:

| Permissão | Papel |
|---|---|
| tax.read, tax.calculate, tax.simulate, tax.opportunities.read, tax.opportunities.review, tax.credits.manage | `Management` (Gerente, Diretoria) |
| tax.opportunities.approve; nova versão do perfil (mudança de regime) | `Directors` (Diretoria) |
| tax.rules.manage (regras e fontes jurídicas; revisão profissional; aprovação interna para piloto e sua retirada) | policy `Platform` (operador do SaaS) |

Vendedor, Estoquista e Cozinha não acessam o módulo. O teste `RbacCoverageTests` garante que nenhum endpoint fica sem papel.

## Segurança (threat review)

| Ameaça | Mitigação |
|---|---|
| Vazamento entre tenants | Todas as tabelas de dados são `StoreEntity` (filtro tenant + loja); id de outro tenant → 404. Locks de linha (`FOR UPDATE`) também filtram `tenant_id`. Teste de isolamento cobre cálculo, verificação, oportunidade, revisão, crédito, listas e perfil. |
| IDOR | Nenhuma rota aceita tenant ou loja do cliente; vêm do token. Ids de filhos (requisito) são buscados dentro do agregado já filtrado. |
| Mass assignment | Requests são records com só os campos permitidos; status, autoria, hashes e ids são definidos no servidor. |
| Injeção | EF parametrizado; SQL cru só com `ExecuteSqlInterpolatedAsync` (parametrizado). Regras não executam expressões: o vocabulário é fechado e tipado, e JSON fora dele é recusado (422). |
| Serialização | Desserialização polimórfica só nos tipos declarados (`JsonDerivedType`); discriminador desconhecido falha. |
| Alteração de regras | Só policy `Platform`; versão ativa é imutável (só status muda); ativar exige todas as fontes conferidas; toda mudança é auditada com estado anterior e posterior. |
| Aprovação indevida | Aprovação só Diretoria; exige revisão, todos os requisitos atendidos, reconhecimento explícito de risco alto; estrutura bloqueada nunca é aprovável; lock de linha evita aprovações concorrentes. |
| Uso duplo de crédito | Lock de linha por crédito + verificação de saldo + índice único (crédito, obrigação) para usos. |
| Exposição de documentos | Evidências guardam descrição, referência e hash; nenhum arquivo é armazenado nesta versão. |
| Logs | Logs estruturados com ids, contagens, hashes e versão da engine — sem CPF/CNPJ nem valores de documentos de terceiros. |
| Abuso de recursos | Limites nos validadores: 500 linhas, 10 cenários, 3 hipóteses, listas com `take` limitado. |

## Performance

- Lookup temporal por índice `(status, valid_from)`; o catálogo inteiro é pequeno (dezenas de versões) e é lido uma vez por cálculo, sem N+1 (versões + fontes em duas consultas).
- Índices por `(tenant_id, store_id, …)` em todas as listas; paginação por `take` com teto.
- Simulações em memória; a única gravação é a própria simulação.
- Não há cache do catálogo nesta versão (custo baixo); quando houver, a chave deve incluir a data e invalidar a cada mudança de status — o catálogo é global, então não há risco de cache cruzado entre tenants.

## Observabilidade

`ILogger` estruturado (Serilog JSON da aplicação) em: cálculo criado (id, origem, nº de regras e componentes, engine, hash do input), oportunidades identificadas, bloqueio por guardrail (warning), aprovação, divergência de split (warning), criação e mudança de status de regra (warning), verificação de reprodução. Falhas sobem pelo middleware existente (`error_events`).
