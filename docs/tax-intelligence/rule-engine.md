# Engine de regras

Código: `nexo-backend/src/Nexo.Application/Modules/TaxIntelligence/Engine/`. Versão: `TaxEngine.Version = "orken-tax-engine/1.0.0"`.

## Princípios

- **Determinística:** nenhuma leitura de relógio, banco, ambiente ou aleatoriedade durante a execução; mensagens e números formatados com cultura invariante; a ordem do catálogo não importa.
- **Sem conhecimento embutido:** a engine não sabe nenhuma alíquota. Cada parte da base e cada alíquota vêm de uma versão de regra.
- **Vocabulário fechado e tipado:** condições e resultados são records C# serializados com discriminador `kind`. Não existe DSL de expressões; um novo tipo de condição é código revisado.
- **IA não calcula.** Modelos de linguagem podem, no futuro, sugerir classificações ou explicar resultados, mas nunca produzem o número oficial.

## Vocabulário

Condições (`TaxCondition`):

| `kind` | Passa quando | Observação |
|---|---|---|
| `supplier-regime-in` / `supplier-regime-not-in` | o regime do **fornecedor** está (não está) na lista | fornecedor = o estabelecimento na saída, a contraparte na entrada; regime desconhecido → indeterminado |
| `direction-is` | a operação é de saída/entrada | |
| `item-kind-in` | o item é bem/serviço | só regras de linha |
| `item-general-regime` | o item está no regime geral | itens com `specialTreatment` (regime diferenciado/específico) não recebem alíquota nesta versão |
| `profile-flag-is` | o fato declarado no perfil tem o valor | lista fechada: `accessoryObligationsCompliant`; não declarado → indeterminado |

Resultados (`TaxRuleResult`):

| `kind` | Fase | Efeito |
|---|---|---|
| `base-inclusion` | base | o componente fica na base (acréscimo é somado; desconto não é deduzido) |
| `base-exclusion` | base | o componente sai da base (desconto/tributo no preço é deduzido) |
| `rate` | alíquota | `tax` × `ratePercent` sobre a base da linha |
| `note` | nota | aviso sobre a operação inteira (ex.: dispensa de recolhimento) |

Componentes da base: `Surcharges`, `UnconditionalDiscount`, `ConditionalDiscount`, `ExcludedTaxes`. Cada linha começa no **preço bruto** (`gross`).

## Avaliação

1. **Seleção temporal:** para cada chave de regra, a versão de maior número com status `Active` que cobre a data de referência (`valid_from ≤ data ≤ valid_to`).
2. Por linha: regras de **base** (ordem: fase, prioridade, chave), depois regras de **alíquota** sobre a base resultante (arredondada a centavos, meio para longe do zero — convenção desta versão da engine).
3. Regras de **nota** sobre a operação inteira.
4. Versões que cobrem a data mas estão `PendingRegulation`, `Unverified` ou `Draft` são listadas em `notApplied` — nunca aplicadas.
5. Condição indeterminada (fato faltando) → a regra não se aplica e a saída traz um aviso dizendo por quê.
6. Duas regras ativas dando alíquota ao mesmo tributo na mesma linha → `tax_rule_conflict` (422). O catálogo está inconsistente e não se escolhe uma ao acaso.

Cada regra examinada vira um `TaxRuleExecution`: chave, versão, hash do conteúdo, linha, se casou, cada condição com esperado/real/resultado, saída e fontes.

## Versionamento

- `TaxRule` = identidade estável (chave única, nunca reutilizada).
- `TaxRuleVersion` = conteúdo (condições, resultado, vigência, prioridade) **fixo na criação**, com `content_hash` (SHA-256 da forma canônica). Depois disso só o status anda: `Draft/PendingRegulation/Unverified → Active → Superseded/Revoked`. Nada volta.
- Ativar uma versão exige que **todas** as suas fontes estejam `Verified`. Versões ativas mais antigas totalmente cobertas pela nova viram `Superseded` — continuam no banco e podem ser recarregadas para reproduzir cálculos antigos.
- Diferença entre os casos pedidos:

| Caso | Como fica |
|---|---|
| Regra implementada e validada | versão `Active` com fontes `Verified` |
| Regra parametrizável | nova versão com outros parâmetros (alíquota, vigência) |
| Hipótese de simulação | regra sintética `HYPOTHESIS.*`, só em memória, rotulada na saída (`isHypothesis`) |
| Oportunidade | não é regra: proposta de detector, revisada por pessoas |
| Regra ainda não regulamentada | versão `PendingRegulation` (ex.: CBS 2027–2033, que depende de resolução do Senado) |

## Como criar uma regra nova

Pela API da plataforma (operador do SaaS, policy `Platform`):

1. `POST /api/platform/tax/legal-sources` — cadastra a fonte com o **texto literal** e a URL oficial (https).
2. `POST /api/platform/tax/legal-sources/{code}/verify` — registra quem conferiu o texto com a fonte oficial.
3. `POST /api/platform/tax/rules` — cria a identidade (`key`, nome, descrição).
4. `POST /api/platform/tax/rules/{key}/versions` — cria a versão como rascunho:

```json
{
  "validFrom": "2027-01-01", "validTo": "2028-12-31", "priority": 20,
  "conditions": [ { "kind": "supplier-regime-not-in", "regimes": ["SimplesNacional", "SimplesNacionalExcessoSublimite", "Mei"] },
                  { "kind": "item-general-regime" } ],
  "result": { "kind": "rate", "tax": "IbsState", "ratePercent": 0.05 },
  "legalSourceCodes": ["LC214-2025-ART344"],
  "notes": "Por que esta versão existe"
}
```

5. `POST /api/platform/tax/rule-versions/{id}/status` com `{ "status": "Active" }` (ou `PendingRegulation`/`Unverified`).

No código (catálogo semeado): **nunca edite `TaxCatalogV1`**. Conteúdo novo entra por uma nova migration com uma classe `TaxCatalogV2` (ou pela API). O teste `Catalogue_v1_is_frozen` falha se o v1 mudar.

## Como testar uma regra

- **Unitário (engine):** monte um `TaxEngineInput` e a lista de `TaxEngineRule` e verifique `TaxEngine.Run` — veja `tests/Nexo.UnitTests/TaxIntelligence/TaxEngineTests.cs` (`TaxTestData.Catalogue()` dá o catálogo semeado como a engine o vê).
- **Integração:** `tests/Nexo.IntegrationTests/TaxIntelligence/TaxIntelligenceTests.cs`, teste `A_new_rule_version_never_changes_a_past_calculation`, mostra o ciclo completo pela API da plataforma numa data isolada.
- Toda regra nova deve ter ao menos: um caso em que casa, um em que não casa, e um em que o fato está faltando.

## Mudando a engine

Qualquer mudança de semântica de avaliação ou da forma canônica **exige nova `TaxEngine.Version`**. A verificação (`/verify`) acusa quando a versão da engine do cálculo difere da atual. Para manter reprodutibilidade de cálculos antigos com engine nova, a versão antiga do avaliador deve ser preservada (estratégia por versão) — ainda não necessário na v1.
