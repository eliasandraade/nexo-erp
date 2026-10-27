# Modelo de auditoria

A pergunta que orienta o modelo: **daqui a cinco anos, numa fiscalização, dá para mostrar como este número foi calculado e por que esta decisão foi tomada?**

## O que torna um cálculo reconstruível

Cada `TaxCalculation` guarda:

| Campo | O que é |
|---|---|
| `input` | o `TaxEngineInput` canônico (data, fatos do perfil vigente, direção, regime da contraparte, linhas) |
| `input_hash` | SHA-256 do texto de `input` |
| `rule_set_hash` | SHA-256 da lista `chave@versão#hash_do_conteúdo` das regras ativas selecionadas |
| `engine_version` | versão exata da engine que rodou (ex.: `orken-tax-engine/1.0.0`); define também a forma canônica dos documentos e hashes |
| `calculation_key` | hash(engine, input, regras, origem, fatos) — idempotência |
| `output` / `output_hash` | o `TaxEngineOutput` completo e seu SHA-256 |
| `profile_version_id` | a versão do perfil usada |
| `tax_rule_executions` | uma linha por regra examinada, com condições, saída, fontes e o hash do conteúdo da versão |

Os documentos canônicos são colunas **`json`** (não `jsonb`): o Postgres guarda o texto byte a byte, então o hash pode ser recalculado a partir do que está gravado. (`jsonb` reordena chaves e quebraria tanto o hash quanto o discriminador polimórfico.)

### Reproduzir um cálculo histórico

A cadeia é **Engine V1 → Canonical V1 → hashes V1**. Cada versão da engine (`ITaxEngine`) tem a sua forma canônica (`ITaxCanonicalizer`, V1 = `orken-canonical/1`), congeladas juntas. O `TaxEngineRegistry` lista as versões que a build sabe executar; cálculos novos usam a atual, e um cálculo gravado é lido, re-detectado e verificado **somente** pela versão que ele registra.

`POST /api/tax/calculations/{id}/verify` (não grava nada):

1. procura no registry a engine de `engine_version`. **Se ela não estiver registrada, a resposta é `engineAvailable = false`, `reproducible = false`, sem `recomputedOutputHash`** — a engine atual nunca é usada no lugar dela;
2. com a forma canônica dessa engine, confere `hash(input) == input_hash` e `hash(output) == output_hash`;
3. recarrega **exatamente** as versões de regra que o cálculo examinou (inclusive as hoje `Superseded`) e as listadas como não aplicadas, com o status que tinham, lendo o conteúdo com a mesma forma canônica;
4. confere que o conteúdo de cada versão ainda tem o mesmo hash;
5. roda **essa** engine e compara o novo hash do output com o armazenado.

Resposta: `reproducible`, `engineAvailable`, as versões da engine (gravada e atual) e a lista de diferenças. Uma engine atual diferente da gravada **não** é diferença: é o caso normal depois de uma V2.

**`engine_version` sozinho não garante reprodução.** Reproduzir exige, ao mesmo tempo: (a) a implementação daquela versão ainda registrada na build; (b) a forma canônica daquela versão; (c) as versões de regra usadas, intactas (`content_hash` igual); (d) `input` e `output` gravados intactos. Faltando qualquer um, a verificação diz o que faltou em vez de dar um resultado.

Pelo banco, o mesmo é possível sem a API: o `input`, o `engine_version` e os `rule_version_id` das execuções bastam, desde que a implementação daquela engine exista.

### Catálogo inicial (migration autocontida)

A migration `TaxIntelligenceFoundation` insere o catálogo V1 como **linhas literais** (ids, textos, JSON canônico, hashes, datas), num arquivo próprio da migration (`*.CatalogV1.cs`) que não referencia engine, catálogo, serializador, DTO nem domínio. Uma mudança futura de código não muda o que essa migration insere. Testes unitários conferem que as linhas literais batem com a descrição tipada `TaxCatalogV1` sob `orken-canonical/1`, fixam os hashes e uma impressão digital de todo o seed, e varrem o código da migration atrás de referências proibidas.

## Imutabilidade

| Entidade | Regra |
|---|---|
| `TaxRuleVersion` | conteúdo fixo na criação; só o status muda, cada mudança auditada |
| `TaxLegalSource` | texto literal não muda; nova redação da lei = nova fonte |
| `TaxProfileVersion` | só se acrescenta; correção = nova versão |
| `TaxTransaction` / `TaxCalculation` / componentes / execuções | gravados uma vez |
| `TaxCreditLedgerEntry` | só se acrescenta; saldo = soma dos lançamentos |
| `SplitSettlement` | só se acrescenta. Corrigir uma retenção = **estorno**: nova linha com valor negativo, `reverses_settlement_id` apontando a original e motivo obrigatório (um estorno não pode ser estornado); a original fica. A conciliação soma todas as linhas e conta como "vivas" só as que não foram estornadas nem são estorno — sem nenhuma viva, a expectativa volta a `Pending`. Depois, a retenção certa entra como outra liquidação |
| `TaxRuleVersion.professional_review*` | a revisão profissional muda por registro auditado; nunca altera conteúdo nem status técnico |
| `TaxDecision` | gravada uma vez, com `dossier_hash` |
| `TaxOpportunity` | estado anda pelo workflow; cada passo auditado com antes/depois |

Nenhuma tabela do módulo tem exclusão por API. FKs de cálculos para versões de regra e de perfil são `Restrict`.

## Trilha de auditoria

Eventos vão para a tabela existente `audit_records` via `IAuditWriter`, na mesma transação da mudança (gravados juntos ou nenhum dos dois). Cada evento traz: quem (`actor_id`, `actor_name`, tipo `user` ou `platform`), quando, tenant, tipo e id da entidade (`Tax*`), descrição legível e metadados com `storeId`, `requestId` (o `X-Request-Id` da requisição, que liga ao log) e detalhes (antes/depois quando há mudança de estado).

Ações (`TaxAuditActions`): `tax_profile_version_created`, `tax_calculation_created`, `tax_simulation_created`, `tax_opportunity_identified`, `tax_opportunity_review_started`, `tax_opportunity_requirement_answered`, `tax_opportunity_evidence_added`, `tax_opportunity_blocked`, `tax_opportunity_rejected`, `tax_opportunity_approved`, `tax_credit_registered`, `tax_credit_movement`, `tax_split_expectations_created`, `tax_split_settlement_recorded`, `tax_split_settlement_reversed`, `tax_legal_source_created`, `tax_legal_source_verified` (texto conferido), `tax_rule_created`, `tax_rule_version_created`, `tax_rule_version_status_changed`, `tax_rule_version_professional_review`.

A aprovação registra também se houve **auto-aprovação** (quem aprovou fez a análise sozinho), a justificativa e as regras com revisão profissional pendente.

## Dossiê de decisão

Na aprovação, `TaxDecision.dossier` congela:

- estratégia e dados da oportunidade (tipo, natureza do impacto, impacto estimado, confiança, ressalvas, detector);
- fatos: id do cálculo, data, hashes, versão da engine e o input completo;
- cenário atual e cenário proposto (com os outputs da engine);
- regras aplicadas (chave, versão, id, hash do conteúdo, fontes);
- fontes jurídicas com **o texto literal**, a URL e a conferência de texto (`textVerification` — o trecho reproduz a lei; não é parecer);
- revisão profissional: as regras ainda sem aprovação de profissional tributário e se a política exigia aprovação;
- requisitos como foram respondidos (quem, quando, nota);
- evidências (descrição, referência do documento, hash, quem anexou);
- risco, reconhecimento de risco alto, quem iniciou a análise, aprovador, data;
- segregação de funções: se era exigida, se foi auto-aprovação e a justificativa;
- versão da engine que produziu os cenários.

`dossier_hash` = SHA-256 do texto do dossiê, na forma canônica da engine que produziu a oportunidade. A aprovação **não executa nada**: não muda venda, preço, nota, livro ou crédito. Executar a estratégia é decisão do negócio, fora do sistema.
