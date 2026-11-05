# Operações suportadas pela engine V1 (fail closed)

Antes de qualquer cálculo do sistema, a matriz `TaxCoveragePolicyV1` (`orken-coverage/2`) decide se o padrão de fatos da operação está no recorte que a V1 modela. Fora dele **nada é calculado** — nunca um zero. A API responde `422 tax_operation_not_supported` com o status (`Unsupported`, `RequiresClassification`, `RequiresProfessionalReview` ou `PendingRegulation`) e cada motivo em `details`. A tentativa fica gravada na auditoria (`tax_calculation_refused`). `POST /api/tax/calculations/coverage` mostra a decisão sem calcular nada.

Esta matriz descreve o que o software modela. Não é parecer tributário, e a lista está pendente de revisão profissional.

## Recorte da V1

| Dimensão | Suportado | Fora do recorte |
|---|---|---|
| Data do fato | 2026 (alíquotas de teste dos arts. 343 e 346) | antes de 2026 → `Unsupported`; 2027 em diante → `PendingRegulation` |
| Padrão temporal | `SingleSupply`, com data da operação = data do fornecimento = data do documento | sem padrão informado → `RequiresProfessionalReview`; fornecimento continuado, antecipação, entrega futura e aquisição pública → `Unsupported`; `Other` → `RequiresProfessionalReview` |
| Datas | iguais à data da operação | data de fornecimento ou de documento diferente → `RequiresProfessionalReview`; pagamento antes do fornecimento → `Unsupported`; período de execução informado → `Unsupported` |
| Regime do estabelecimento | regime regular; Simples Nacional dentro do sublimite **com a opção pelo IBS/CBS informada** | situação no Simples desconhecida, MEI, SIMEI, Simples acima do sublimite, ou opção pelo regime regular do IBS/CBS não informada → `RequiresProfessionalReview` (nunca lidos como regime regular) |
| Contraparte (entrada) | regime informado, fora de MEI/SIMEI e de Simples acima do sublimite | regime não informado → `Unsupported`; MEI/SIMEI, Simples acima do sublimite ou situação desconhecida → `RequiresProfessionalReview` |
| Item | sem regime diferenciado ou específico | item com `specialTreatment` → `Unsupported` |
| Classificação | **mercadoria:** NCM + cClassTrib; **serviço:** NBS + cClassTrib; o cClassTrib **ativo no catálogo na data** da operação | falta o código exigido → `RequiresClassification`; código fora do catálogo ativo na data → `RequiresClassification`; no modo `ProfessionalFinal`, código sem revisão profissional aprovada → `RequiresProfessionalReview` |

Sobre a classificação:

- A V1 **registra** o cClassTrib, mas não o interpreta. O catálogo (`/platform/tributos?aba=classificacao`) é importado de fonte documentada como rascunho e ativado explicitamente; só código ativo na data entra no recorte. Quais códigos são adequados a cada produto é responsabilidade da empresa e, no modo definitivo, exige revisão profissional do código.
- Não se exige NCM, NBS e cClassTrib de qualquer item. Exige-se o que basta para o recorte, conforme o tipo de item.
- A venda do PDV lê a classificação tributária do produto (`/tributos/classificacao`). Produto sem classificação continua recusado com `RequiresClassification`, em vez de ser calculado presumindo o regime geral.

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
