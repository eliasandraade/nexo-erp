# Roadmap e limitações

## Limitações atuais (v1)

| Área | Limitação | Risco |
|---|---|---|
| Catálogo | Só IBS/CBS de 2026 e regras de base do art. 12. 2027+ guardado como pendente/não verificado | cálculos de 2027+ saem sem alíquota (com aviso) |
| Regimes diferenciados/específicos | Itens marcados não são calculados | quem tem cesta básica, saúde, educação etc. vê só aviso |
| Classificação de itens | Classificação tributária por produto (cClassTrib, NCM/NBS) lida pelas vendas; catálogo cClassTrib versionado na plataforma, fail-closed para código inativo | quais códigos servem a cada produto depende da empresa e do profissional |
| Tributos no preço | Vendas não informam ICMS/ISS/PIS/Cofins embutidos | base superestimada (avisado) |
| Créditos | Ledger gerencial; elegibilidade declarada, não decidida; sem ligação automática com compras | depende de quem lança |
| Split payment | Nenhum provedor integrado; retenção real registrada à mão | conciliação manual |
| Maker-checker | Por risco: baixo = auto-aprovação com justificativa; médio = outro revisor identificado; alto = aprovador independente. Acima de `MaterialityThreshold`, o aprovador independente é obrigatório. A justificativa nunca substitui a independência | o valor de materialidade é decisão do dono (sem valor configurado, não há regra de materialidade) |
| Regras por tenant | Não há regras específicas de um tenant (benefício próprio, regime especial) | benefícios só como texto no perfil |
| Engine | Registry por versão; V1 congelada. Uma V2 com outra forma canônica precisa decidir como ler o JSON de regras gravado | documentado em rule-engine.md |
| Revisão jurídica | Catálogo e textos não passaram por profissional tributário; todo o catálogo V1 está `ProfessionalReview = Pending` | com a política de produção, **nenhuma oportunidade pode ser aprovada** até a revisão |
| Obrigações acessórias | Avaliação histórica por período e escopo (`Unknown`, `Declared`, `Verified`, `InconsistencyIdentified`, `UnderRemediation`, `RemediationVerified`, `NonCompliant`, `NotApplicable`). Só `Verified` conta para regras. A participação no PNCT é registrada, mas não equivale a regularidade | quais documentos comprovam o cumprimento depende do profissional |
| Recorte da V1 | Matriz fail-closed ([supported-operations.md](supported-operations.md)): 2026, fornecimento único, classificação suficiente, sem MEI/SIMEI nem Simples acima do sublimite | vendas do PDV recusadas até o cadastro do produto ter cClassTrib |
| Evidências | Tipadas, vinculadas ao requisito, revisadas, substituídas (versões) e revogadas. Arquivo privado opcional (PDF, imagem, XML) com tipo conferido pelos bytes e SHA-256 dos bytes; download autorizado e auditado | sem antivírus configurado, os arquivos ficam marcados "não verificado" (`RequireScan` recusa upload até haver scanner) |
| Créditos | Ciclo de vida: potencial → requisitos em revisão → a apropriar → apropriado/usado/bloqueado/estornado/vencido; descartado. Potencial nunca é disponível | elegibilidade não é decidida pelo sistema |
| Split payment | Expectativa gerencial; liquidação parcial, estorno, divergência, idade, filtros e CSV; abstração de provedor sem provedor falso | sem provedor integrado — liquidação registrada à mão |
| Retenção | Motor de política versionado (padrão da plataforma + ajuste da empresa quando permitido), legal hold e prévia; **nenhum descarte automático** | prazos dependem de definição jurídica — ver [audit-model.md](audit-model.md#retenção) |
| Produto | Empacotamento comercial (módulo pago? plano?) não decidido | decisão do dono |

## Política de aprovação

- **`TaxIntelligence:Policy:Enabled`** (kill switch, padrão `false`): desligado, nenhum cálculo, simulação, detecção, decisão ou verificação de execução roda; a consulta e a reprodução do que já foi registrado continuam.
- **`TaxIntelligence:Policy:Mode`** escolhe que aprovação uma regra precisa:
  - `Disabled` (padrão): nenhuma aprovação vale, então nada roda onde a aprovação é exigida;
  - `ControlledPilot`, só por configuração explícita: basta a aprovação interna válida (`InternalApproval = ApprovedForControlledPilot`) ou uma profissional, e a empresa ou loja precisa estar no escopo do piloto mantido pela plataforma;
  - `ProfessionalFinal` (produção definitiva): só `ProfessionalReview = Approved` ou `ApprovedWithCaveats`, dentro da validade.

  Em todos os modos, a regra precisa estar tecnicamente `Active` agora.
- **`RequireApprovedRulesForCalculations`**: um cálculo só roda se todas as regras ativas que a engine pode aplicar na data estiverem aprovadas para o modo. Caso contrário, é recusado com 422 e a recusa fica na auditoria.
- **`RequireApprovedRulesForDecisions`**: uma oportunidade só é aprovada se todas as regras que casaram no cálculo estiverem aprovadas para o modo. Caso contrário, 422 e nada é gravado.

Os códigos de recusa são:

- `tax_intelligence_disabled` (kill switch);
- `tax_approval_mode_disabled` (modo não configurado);
- `tax_pilot_scope_disabled`, `tax_pilot_scope_expired`, `tax_pilot_scope_tenant` e `tax_pilot_scope_store` (escopo do piloto);
- `tax_pilot_approval_missing` no piloto;
- `tax_professional_review_pending` no modo definitivo;
- `tax_calculation_not_reproducible` (decisão sobre um cálculo que não reproduz mais).

As duas políticas ficam ligadas por padrão fora de Development e Testing. Nesses dois ambientes, o código relaxa só a exigência de aprovação, nunca o `Enabled` nem o `Mode`, para o fluxo ser exercitável com qualquer catálogo. Configuração explícita sempre vence. Testes de integração ligam as políticas e provam o que cada modo aceita e recusa. O maker-checker (`LowRisk`, `MediumRisk`, `HighRisk`, `MaterialityThreshold`) vale em todos os ambientes.

O registro da decisão do piloto, com as versões aprovadas, as ressalvas, os riscos aceitos e os critérios de suspensão, está em [homologation/internal-pilot-approval-2026.md](homologation/internal-pilot-approval-2026.md).

## O que depende do profissional tributário

1. Registrar formalmente a revisão recebida (o "apto após ajustes") nas versões do catálogo, pelo endpoint de revisão profissional, com revisor, identificação, escopo, validade e ressalvas. O resultado será, no máximo, `ReviewReceived` ou `ChangesRequired`, conforme a regra. Nada foi marcado automaticamente.
1. Aprovar, pedir ajuste ou rejeitar cada versão do catálogo V1 (todas `Pending`), incluindo `IBSCBS.NOTE.2026-WAIVER` v2.
1. Definir quais códigos cClassTrib ficam dentro do recorte de regime geral da V1.
1. Dizer como MEI/SIMEI, Simples acima do sublimite e a opção pelo regime regular do IBS/CBS (Resoluções CGSN 186 e 194/2026) devem ser tratados.
1. Ler o Ato Conjunto RFB/CGIBS nº 5/2026 (PNCT) e dizer o que uma regularização verificada significa para as regras.
2. Dizer quais documentos comprovam o cumprimento das obrigações acessórias de 2026 (o que justifica `Verified` numa avaliação).
3. Validar os requisitos e as ressalvas do detector de desconto incondicional e se, em algum período, a redução calculada é economia.
4. Revisar as decisões de interpretação listadas em [legal-sources.md](legal-sources.md#decisões-de-interpretação-para-revisão-profissional).
5. Ler o inventário de fontes oficiais (LC 227/2026, Decreto 12.955/2026, Atos Conjuntos e Técnicos) e dizer o que muda no catálogo.
6. Revisar o texto da nota da dispensa de 2026 v2.
7. Revisar os textos das telas.

## Próximos passos

1. **Revisão profissional** — ver a lista acima.
2. **Classificação fiscal de itens:** regime diferenciado/específico e NBS no cadastro de produto e serviço → condição por classificação.
3. **Reduções do art. 348, III, a** (e anexos da LC 214) como regras parametrizáveis, por classificação.
4. **Alíquotas de 2027+** assim que a resolução do Senado existir; ativar IBS 2027–2028 depois de verificar o Simples.
5. **Compras → créditos:** gerar `TaxCredit` a partir de compras com chave de NF-e; regra de elegibilidade versionada (art. 47, 57).
6. **Procurement:** usar `supplier-comparison` na tela de cotação de compras.
7. **Split payment:** adapter real quando houver API dos PSPs/adquirentes; importação de relatórios de liquidação.
8. **Mais detectores:** créditos não apropriados, retenção excedente no split (restituição), diferimento por data de fornecimento — sempre com natureza de impacto explícita.
9. **Evidências com arquivo** usando o storage privado (prefixo do tenant, URL assinada).
10. **Assistência por modelo de linguagem** (classificação de documentos, explicação de resultados) — sempre como sugestão revisada, nunca como cálculo.
11. **Cache do catálogo** por data quando o volume justificar.
12. **Regras por tenant** (benefícios e regimes especiais próprios) com o mesmo versionamento.
