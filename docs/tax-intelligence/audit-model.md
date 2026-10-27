# Modelo de auditoria

A pergunta que orienta o modelo: **daqui a cinco anos, numa fiscalização, dá para mostrar como este número foi calculado e por que esta decisão foi tomada?**

## O que torna um cálculo reconstruível

Cada `TaxCalculation` guarda:

| Campo | O que é |
|---|---|
| `input` | o `TaxEngineInput` canônico (data, fatos do perfil vigente, direção, regime da contraparte, linhas) |
| `input_hash` | SHA-256 do texto de `input` |
| `rule_set_hash` | SHA-256 da lista `chave@versão#hash_do_conteúdo` das regras ativas selecionadas |
| `engine_version` | versão da engine que rodou |
| `calculation_key` | hash(engine, input, regras, origem, fatos) — idempotência |
| `output` / `output_hash` | o `TaxEngineOutput` completo e seu SHA-256 |
| `profile_version_id` | a versão do perfil usada |
| `tax_rule_executions` | uma linha por regra examinada, com condições, saída, fontes e o hash do conteúdo da versão |

Os documentos canônicos são colunas **`json`** (não `jsonb`): o Postgres guarda o texto byte a byte, então o hash pode ser recalculado a partir do que está gravado. (`jsonb` reordena chaves e quebraria tanto o hash quanto o discriminador polimórfico.)

### Reproduzir um cálculo histórico

`POST /api/tax/calculations/{id}/verify` (não grava nada):

1. confere `hash(input) == input_hash` e `hash(output) == output_hash`;
2. recarrega **exatamente** as versões de regra que o cálculo examinou (inclusive as hoje `Superseded`) e as listadas como não aplicadas, com o status que tinham;
3. confere que o conteúdo de cada versão ainda tem o mesmo hash;
4. roda a engine e compara o novo hash do output com o armazenado.

Resposta: `reproducible`, as versões da engine (gravada e atual) e a lista de diferenças.

Pelo banco, o mesmo é possível sem a API: o `input` e os `rule_version_id` das execuções bastam.

## Imutabilidade

| Entidade | Regra |
|---|---|
| `TaxRuleVersion` | conteúdo fixo na criação; só o status muda, cada mudança auditada |
| `TaxLegalSource` | texto literal não muda; nova redação da lei = nova fonte |
| `TaxProfileVersion` | só se acrescenta; correção = nova versão |
| `TaxTransaction` / `TaxCalculation` / componentes / execuções | gravados uma vez |
| `TaxCreditLedgerEntry` | só se acrescenta; saldo = soma dos lançamentos |
| `SplitSettlement` | só se acrescenta; correção = outra liquidação com outra referência |
| `TaxDecision` | gravada uma vez, com `dossier_hash` |
| `TaxOpportunity` | estado anda pelo workflow; cada passo auditado com antes/depois |

Nenhuma tabela do módulo tem exclusão por API. FKs de cálculos para versões de regra e de perfil são `Restrict`.

## Trilha de auditoria

Eventos vão para a tabela existente `audit_records` via `IAuditWriter`, na mesma transação da mudança (gravados juntos ou nenhum dos dois). Cada evento traz: quem (`actor_id`, `actor_name`, tipo `user` ou `platform`), quando, tenant, tipo e id da entidade (`Tax*`), descrição legível e metadados com `storeId`, `requestId` (o `X-Request-Id` da requisição, que liga ao log) e detalhes (antes/depois quando há mudança de estado).

Ações (`TaxAuditActions`): `tax_profile_version_created`, `tax_calculation_created`, `tax_simulation_created`, `tax_opportunity_identified`, `tax_opportunity_review_started`, `tax_opportunity_requirement_answered`, `tax_opportunity_evidence_added`, `tax_opportunity_blocked`, `tax_opportunity_rejected`, `tax_opportunity_approved`, `tax_credit_registered`, `tax_credit_movement`, `tax_split_expectations_created`, `tax_split_settlement_recorded`, `tax_legal_source_created`, `tax_legal_source_verified`, `tax_rule_created`, `tax_rule_version_created`, `tax_rule_version_status_changed`.

## Dossiê de decisão

Na aprovação, `TaxDecision.dossier` congela:

- estratégia e dados da oportunidade (tipo, natureza do impacto, impacto estimado, confiança, ressalvas, detector);
- fatos: id do cálculo, data, hashes, versão da engine e o input completo;
- cenário atual e cenário proposto (com os outputs da engine);
- regras aplicadas (chave, versão, id, hash do conteúdo, fontes);
- fontes jurídicas com **o texto literal** e a URL;
- requisitos como foram respondidos (quem, quando, nota);
- evidências (descrição, referência do documento, hash, quem anexou);
- risco, reconhecimento de risco alto, aprovador, data, versão da engine.

`dossier_hash` = SHA-256 do texto do dossiê. A aprovação **não executa nada**: não muda venda, preço, nota, livro ou crédito. Executar a estratégia é decisão do negócio, fora do sistema.
