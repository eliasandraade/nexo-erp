# Operações suportadas pela engine V1 (fail closed)

Antes de qualquer cálculo do sistema, a matriz `TaxCoveragePolicyV1` (`orken-coverage/1`) decide se o padrão de fatos da operação está no recorte que a V1 modela. Fora dele **nada é calculado** — nunca um zero. A API responde `422 tax_operation_not_supported` com o status (`Unsupported`, `RequiresClassification`, `RequiresProfessionalReview` ou `PendingRegulation`) e cada motivo em `details`. A tentativa fica gravada na auditoria (`tax_calculation_refused`). `POST /api/tax/calculations/coverage` mostra a decisão sem calcular nada.

Esta matriz descreve o que o software modela. Não é parecer tributário, e a lista está pendente de revisão profissional.

## Recorte da V1

| Dimensão | Suportado | Fora do recorte |
|---|---|---|
| Data do fato | 2026 (alíquotas de teste dos arts. 343 e 346) | antes de 2026 → `Unsupported`; 2027 em diante → `PendingRegulation` |
| Padrão temporal | `SingleSupply`, com data da operação = data do fornecimento = data do documento | sem padrão informado → `RequiresProfessionalReview`; fornecimento continuado, antecipação, entrega futura e aquisição pública → `Unsupported`; `Other` → `RequiresProfessionalReview` |
| Datas | iguais à data da operação | data de fornecimento ou de documento diferente → `RequiresProfessionalReview`; pagamento antes do fornecimento → `Unsupported`; período de execução informado → `Unsupported` |
| Regime do estabelecimento | regime regular; Simples Nacional | MEI/SIMEI e Simples acima do sublimite → `RequiresProfessionalReview` (não são tratados como o Simples comum) |
| Contraparte (entrada) | regime informado, fora de MEI e de Simples acima do sublimite | regime não informado → `Unsupported`; MEI/SIMEI ou Simples acima do sublimite → `RequiresProfessionalReview` |
| Item | sem regime diferenciado ou específico | item com `specialTreatment` → `Unsupported` |
| Classificação | **mercadoria:** NCM + cClassTrib; **serviço:** NBS + cClassTrib | falta o código exigido para o tipo de item → `RequiresClassification` (a V1 não presume o regime geral) |

Sobre a classificação:

- A V1 **registra** o cClassTrib, mas não o interpreta. Quais códigos ficam dentro do recorte é uma pendência profissional.
- Não se exige NCM, NBS e cClassTrib de qualquer item. Exige-se o que basta para o recorte, conforme o tipo de item.
- Uma venda do PDV hoje não traz cClassTrib, porque o cadastro do produto ainda não tem esse campo. Por isso a venda é recusada com `RequiresClassification`, em vez de ser calculada presumindo o regime geral.

## Datas que o domínio já distingue

A `TaxTransaction` guarda os campos abaixo, para quando uma versão futura modelar esses casos:

- `supply_pattern`;
- `document_date`;
- `supply_date`;
- `payment_or_advance_date`;
- `competence_period`;
- `execution_from` e `execution_to`.

Na V1, eles só servem para recusar o que não é fornecimento único e imediato. Nenhuma data é presumida em silêncio.

## Simulações

Simulações e comparação de fornecedores são hipóteses rotuladas e não passam pela matriz. Elas nunca viram cálculo do sistema, oportunidade ou dossiê.
