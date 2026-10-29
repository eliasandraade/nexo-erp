# Tax Intelligence — runbook de produção

Como operar o módulo de Inteligência Tributária em produção: ligar, ativar, suspender, diagnosticar, recuperar.
Vale para o código de `feat/tax-intelligence-finalization` em diante.

> **Este documento não ativa nada.** Cada passo de ativação é uma decisão do Product Owner, registrada com referência
> e motivo. O código fica pronto; ligar em produção é um ato separado.

## 1. Estado de partida em produção

Até uma decisão expressa do PO, a produção fica assim:

| Item | Valor | Quem muda |
|---|---|---|
| `TaxIntelligence:Policy:Enabled` | ausente ou `false` | deploy (Railway), com decisão do PO |
| `TaxIntelligence:Policy:Mode` | ausente ou `Disabled` | deploy (Railway), com decisão do PO |
| Ativação na plataforma (`tax_module_activations`) | nenhuma linha | super admin, pelo assistente |
| Escopo do piloto | nenhum | super admin |
| Regras aprovadas para o piloto | nenhuma | super admin, por regra |
| Revisões profissionais | todas `Pending` | só com revisão profissional **real** recebida |

Com isso, toda chamada que cria trabalho novo (cálculo, simulação, detecção, decisão) é recusada com
`tax_intelligence_disabled`. Consultar histórico continua funcionando.

## 2. Configuração (fail-closed)

Seção `TaxIntelligence` (variáveis de ambiente no Railway usam `__`, ex.: `TaxIntelligence__Policy__Enabled`).

| Chave | Padrão | Efeito |
|---|---|---|
| `Policy:Enabled` | `false` | Interruptor geral. `false` recusa todo trabalho novo |
| `Policy:Mode` | `Disabled` | `ControlledPilot` ou `ProfessionalFinal`. Só a configuração escolhe o modo; a tela só ativa o modo configurado |
| `Policy:RequireApprovedRulesForCalculations` | `true` | Cálculo exige regras aprovadas no modo |
| `Policy:RequireApprovedRulesForDecisions` | `true` | Decisão exige regras aprovadas no modo |
| `Policy:LowRisk` / `MediumRisk` / `HighRisk` | auto-aprovação justificada / revisor identificado / aprovador independente | Quem aprova cada risco |
| `Policy:MaterialityThreshold` | ausente | Padrão da instalação; cada empresa pode ter versão própria |
| `Evidence:MaxFileSizeMb` | `10` | Limite por arquivo privado |
| `Evidence:RequireScan` | `false` | `true` recusa upload enquanto não houver antivírus configurado |

**Configuração inválida nunca derruba o ERP.** Valores fora do enum, materialidade não positiva ou combinações
incoerentes viram `ConfigurationProblems`: o módulo recusa trabalho novo com `tax_configuration_invalid`, o
resto do ERP sobe normalmente, e o problema aparece em:

- log de inicialização (`TaxConfigurationStartupLog`), uma linha por problema;
- `GET /api/platform/tax/module` → `configurationProblems`;
- `GET /api/platform/tax/readiness` → item `Blocking`.

## 3. Ordem de ativação do piloto controlado

Pré-requisito: decisão do PO por escrito (referência que vai para cada registro).

1. **Deploy** com `Enabled=true` e `Mode=ControlledPilot`. Nada roda ainda: falta a ativação na plataforma
   (`tax_module_not_activated`).
2. **Fontes**: em `/platform/tributos?aba=fontes`, conferir o texto oficial de cada fonte usada pelas regras
   (quem comparou é informado; quem registrou é a sessão).
3. **Regras**: ativar as versões cujas fontes estão todas conferidas.
4. **Aprovação interna do piloto**, regra por regra (escopo, vigência, ressalvas, referência da decisão).
   Aprovação interna **não é** revisão profissional e a tela diz isso.
5. **Catálogos**: importar e ativar os códigos cClassTrib a partir de fonte documentada; cadastrar as obrigações
   acessórias; padrões de retenção.
6. **Escopo do piloto**: janela, referência e empresas/lojas participantes.
7. **Prontidão**: `/platform/tributos?aba=estado` → nenhuma linha `Blocking`.
8. **Assistente de ativação** (9 passos): modo → pré-condições → regras sem aprovação → ressalvas →
   reconhecimento do modo sombra → referência → motivo → frase `ATIVAR PILOTO CONTROLADO` → revisão.
9. **Smoke** com uma empresa do piloto: um cálculo manual pequeno; conferir `governance.mode = ControlledPilot`
   no resultado e o evento `tax_calculation_created` na auditoria.

### Modo definitivo (`ProfessionalFinal`)

Tecnicamente pronto e **bloqueado**: o assistente e o servidor recusam enquanto qualquer regra exigida não tiver
revisão profissional aprovada válida para a data e para os regimes cobertos. Revisão profissional só se registra
quando foi **de fato recebida** de um profissional; o sistema não verifica o profissional, guarda o nome como
informado e, separadamente, quem registrou e o horário do servidor. Nunca registrar revisão fictícia em produção.

## 4. Freio de emergência

| Situação | Ação | Efeito |
|---|---|---|
| Resultado suspeito, incidente, pedido do PO | **Suspender** (qualquer operador da plataforma, frase `SUSPENDER`) | Trabalho novo recusado na hora (`tax_module_suspended`); histórico disponível |
| Encerrar o piloto | **Desativar** (super admin, frase `DESATIVAR`) | Volta a `Inactive`; reativar exige o assistente inteiro |
| Plataforma indisponível | Deploy com `Enabled=false` | Mesmo efeito, sem depender da tela |
| Retirar uma regra | Retirar a aprovação interna (motivo) ou revogar a versão | Só trabalho novo é afetado; cálculos antigos continuam reproduzíveis |
| Retirar uma empresa | Retirar participante (motivo) | `tax_pilot_scope_tenant` para ela |

Nenhuma dessas ações apaga dados.

## 5. Diagnóstico

- **Prontidão**: `GET /api/platform/tax/readiness` (só plataforma; nunca health check público). Mostra versão
  do motor, política de cobertura (`orken-coverage/2`), armazenamento, regras elegíveis/bloqueantes, catálogos e
  último evento de auditoria.
- **Recusas recentes**: `GET /api/platform/tax/refusals` — ação, código, empresa, quando.
- **Auditoria**: `GET /api/platform/tax/audit?action=…&tenantId=…` (paginada). Ações relevantes:
  `tax_module_activated`, `tax_module_suspended`, `tax_module_deactivated`, `tax_rule_version_pilot_approval`,
  `tax_rule_version_professional_review`, `tax_calculation_refused`, `tax_decision_refused`,
  `tax_evidence_file_uploaded`, `tax_evidence_file_downloaded`, `tax_dossier_exported`.
- **Métricas** (meter `Orken.Api`): `orken.tax.calculations`, `orken.tax.refusals` (por ação e código),
  `orken.tax.coverage_refusals` (por status), `orken.tax.opportunities`, `orken.tax.approvals`,
  `orken.tax.execution_verifications`, `orken.tax.credit_transitions`, `orken.tax.split`,
  `orken.tax.evidence_uploads`, `orken.tax.engine_verifications`.
- **Correlação**: toda resposta leva `X-Request-Id`; erros 5xx mostram esse id na tela. Procure-o nos logs.
- **Reprodução**: `POST /api/tax/calculations/{id}/verify` recalcula com o mesmo motor, regras e entrada e diz se
  reproduz. Use antes de discutir qualquer número.

### Códigos de recusa mais comuns

| Código | Significa | O que fazer |
|---|---|---|
| `tax_intelligence_disabled` | `Enabled=false` | Nada — é o estado esperado até a decisão |
| `tax_configuration_invalid` | configuração incoerente | Ver `configurationProblems` e corrigir no deploy |
| `tax_module_not_activated` | modo configurado, sem ativação | Assistente de ativação |
| `tax_module_suspended` | freio puxado | Investigar; desativar ou reativar pelo assistente |
| `tax_pilot_scope_*` | fora da janela/empresa/loja | Ajustar o escopo, se a decisão permitir |
| `tax_pilot_approval_missing` | regra sem aprovação interna | Aprovar a regra (com decisão) ou não usar |
| `tax_professional_review_pending` | modo definitivo sem revisão | Só uma revisão profissional real resolve |
| `tax_operation_not_supported` | fora da matriz de cobertura | Ver `details`; nunca é zero |

## 6. Arquivos privados

- Bucket R2 privado; chaves `private/tenants/{tenant}/tax/…` e `private/platform/tax/…`. Nunca URL pública.
- Download só pelos endpoints autorizados (`/api/tax/evidence-files/{id}/content`,
  `/api/platform/tax/documents/{id}/content`) com `nosniff`, `no-store`, CSP sandbox e `X-Content-SHA256`.
- O tipo é conferido pelos bytes (PDF, PNG, JPEG, WebP, XML sem conteúdo ativo); extensão precisa bater.
- Armazenamento indisponível → upload recusado com `tax_file_storage_unavailable`; a evidência por referência
  continua funcionando. O resto do módulo não para.
- Nenhum descarte automático. A retenção só gera prévia; retenção legal sempre prevalece.

## 7. Backup e recuperação

### O que precisa de backup

| Dado | Onde | Observação |
|---|---|---|
| Tabelas `nexo.tax_*` | PostgreSQL (Railway) | Inclui ativações, revisões, dossiês congelados, razão de créditos, auditoria |
| `nexo.audit_records` (entidades `Tax*`) | PostgreSQL | Trilha que reconstrói linha do tempo e recusas |
| Objetos `private/**/tax/**` | R2 | Arquivos de evidência e documentos de plataforma |

### Procedimento

1. **Banco**: o backup diário do Railway cobre o schema `nexo`. Antes de cada migração do módulo, gerar um dump
   manual: `pg_dump --schema=nexo --format=custom` (guardar fora do repositório — ver
   `docs/security/2026-10-dumps-and-jwt-secret.md`).
2. **R2**: antes do primeiro upload real, ligar o versionamento do bucket privado e conferir que não há regra de
   expiração (lifecycle) para os prefixos `private/` — item da checklist de release.
3. **Consistência banco ↔ arquivos**: cada arquivo tem o SHA-256 no banco. Após restaurar, comparar uma amostra
   baixando pelo endpoint e conferindo `X-Content-SHA256`.

### Recuperação

1. Suspender o módulo (frase `SUSPENDER`) antes de restaurar qualquer coisa.
2. Restaurar o banco para o ponto escolhido; o EF aplica só migrações aditivas (`__ef_migrations_history`).
3. Restaurar objetos R2 do mesmo ponto (versões).
4. Conferir prontidão e reproduzir 3 cálculos antigos (`/verify`).
5. Reativar pelo assistente — a reativação fica registrada com motivo e referência da recuperação.

### Rollback de código

As migrações do módulo são aditivas; voltar o código para a versão anterior não exige desfazer migração. Com o
módulo desligado (`Enabled=false`), versões antigas e novas convivem com o mesmo banco.

## 8. O que nunca fazer em produção

- Ligar `Enabled`, mudar `Mode`, criar escopo, aprovar regra ou ativar o módulo sem decisão escrita do PO.
- Registrar revisão profissional que não foi recebida, ou trocar `Pending` por `Approved` para "destravar".
- Inventar contador, advogado, CRC, OAB, assinatura, parecer ou homologação.
- Rodar a bateria de aprovações fictícias (`TaxProfessionalFinalTests`) contra o banco de produção — ela existe
  só no banco descartável dos testes de integração.
- Apresentar redução de IBS/CBS calculado como "economia".
