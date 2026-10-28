# ORKEN Tax Intelligence v1 — Internal Controlled Pilot Approval

> **INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT** — não é **PROFESSIONAL TAX OPINION**.
>
> Este registro documenta uma decisão interna de produto e governança da Andrade Systems. **Não constitui parecer jurídico, contábil ou tributário profissional.** Nenhum advogado, contador, parecerista ou consultoria emitiu opinião sobre estas regras, e nenhuma ferramenta de IA aprovou nada. A revisão profissional de todas as regras continua **pendente**.

| Campo | Valor |
|---|---|
| Produto | ORKEN Tax Intelligence v1 |
| Data da decisão | 27/10/2026 |
| Escopo | Vertical slice IBS/CBS 2026: o recorte suportado pela v1 (matriz `orken-coverage/1`, engine `orken-tax-engine/1.0.0`, forma canônica `orken-canonical/1`) |
| Decisão | **Approved for Controlled Pilot** (`InternalApproval = ApprovedForControlledPilot`) |
| Política | `orken-pilot-approval/1` |
| Responsável | Product Owner / Andrade Systems |
| ProfessionalReview | **Pending** para todas as regras |
| Natureza | Decisão interna de produto e governança |
| Aplicação | Exclusivamente ao recorte documentado. Operações não suportadas continuam fail-closed; períodos com regulação pendente continuam bloqueados |

## Estado resultante

```
INTERNAL PILOT APPROVAL: APPROVED
PROFESSIONAL REVIEW:     PENDING
CONTROLLED PILOT:        ENABLED
UNSUPPORTED OPERATIONS:  FAIL CLOSED
PENDING REGULATION:      BLOCKED
```

## Como a decisão está no sistema

- **Duas dimensões separadas em cada versão de regra.**
  - `InternalApproval`: `NotApproved`, `ApprovedForControlledPilot` ou `Withdrawn`. Registra política, quem decidiu (o papel de governança, nunca uma identidade profissional), escopo, validade, ressalvas aceitas e onde a decisão está registrada (este documento).
  - `ProfessionalReview`: segue como estava, `Pending`. Nenhum revisor, registro ou assinatura foi criado.

  Uma regra pode estar com `InternalApproval = ApprovedForControlledPilot` e `ProfessionalReview = Pending` ao mesmo tempo. Isso é esperado e correto.
- **Modo de aprovação** (`TaxIntelligence:Policy:Mode`):
  - `ControlledPilot` (padrão): a aprovação interna para o piloto, ou uma profissional, basta para executar a regra e para sustentar uma decisão;
  - `ProfessionalFinal` (produção definitiva): só uma aprovação profissional registrada basta. A aprovação interna **nunca** vale como aprovação profissional.
- **Execução fail-closed.** Com a política ligada (padrão fora de desenvolvimento e teste), um cálculo só roda se todas as regras ativas que a engine pode aplicar na data estiverem aprovadas para o modo. Caso contrário, é recusado (422 `tax_pilot_approval_missing`) e a recusa fica na auditoria. A decisão sobre uma oportunidade segue a mesma regra.
- **Registro de cada execução.** Cada cálculo grava, fora do input e do output com hash:
  - o modo (`ControlledPilot`);
  - a versão da política (`orken-pilot-approval/1`);
  - se a política estava ligada;
  - por regra, a aprovação interna, as ressalvas aceitas e a revisão profissional vigente.

  A auditoria (`tax_calculation_created`) carrega os mesmos dados, com data/hora, usuário e hashes.
- **Dossiê.** O dossiê da decisão e o da execução trazem a seção `governance`, com as duas dimensões lado a lado e o link para este registro. O dossiê é o *registro auditável da decisão e das premissas utilizadas pelo motor*, não prova definitiva de conformidade fiscal.
- **Gravação.** A aprovação das versões abaixo foi gravada pela migration autocontida `20261028021406_TaxControlledPilotApproval`. Aprovações futuras ou retiradas são feitas pela plataforma e auditadas:
  - `POST /api/platform/tax/rule-versions/{id}/pilot-approval`;
  - `POST /api/platform/tax/rule-versions/{id}/pilot-approval/withdraw`.

## Versões aprovadas internamente para o piloto

Validade da aprovação: 01/01/2026 a 31/12/2026. Ela não vai além da vigência de cada versão e cobre só 2026, mesmo quando a regra vale por mais tempo.

| Regra | Versão | Ressalvas aceitas (gravadas na versão) |
|---|---|---|
| `IBSCBS.BASE.SURCHARGES` | v1 | Acréscimos cobrados com o preço integram a base; o que é acréscimo depende dos fatos informados. Cálculo nominal não é recolhimento nem efeito financeiro. |
| `IBSCBS.BASE.CONDITIONAL-DISCOUNT` | v1 | Desconto condicional permanece na base. Se um desconto é condicional ou incondicional depende da operação e da documentação, não do rótulo informado. |
| `IBSCBS.BASE.UNCONDITIONAL-DISCOUNT` | v1 | Só sai da base o desconto que reduz efetivamente o preço, consta do documento fiscal e não depende de evento posterior. Desconto escrito na NF não é automaticamente incondicional; exige evidência comercial e confirmação na execução. O documento fiscal sozinho não prova toda a realidade comercial. |
| `IBSCBS.BASE.TRANSITION-TAXES` | v1 | ICMS, ISS, PIS e Cofins informados saem da base (2026–2032); esta aprovação cobre apenas 2026. Sem esses valores, a base pode ficar maior que a real. |
| `CBS.RATE.2026-TEST` | v1 | Alíquota nominal de teste de 0,9% em 2026. Cálculo nominal é separado de recolhimento e de efeito financeiro (compensação ou dispensa, art. 348). Não se aplica ao Simples Nacional nem a itens em regime diferenciado ou específico. Classificação fiscal não substitui a análise da operação. |
| `IBS.RATE.2026-TEST-STATE` | v1 | Alíquota nominal de teste de 0,1% (estadual) em 2026; o IBS municipal não é calculado em 2026 nesta versão. Cálculo nominal é separado de recolhimento e de efeito financeiro. Não se aplica ao Simples Nacional nem a itens em regime diferenciado ou específico. |
| `IBSCBS.NOTE.2026-SIMPLES` | v1 | Nota informativa: o IBS/CBS dentro do Simples Nacional não é calculado. MEI/SIMEI e Simples acima do sublimite continuam dependendo de revisão profissional e não são calculados. |
| `IBSCBS.NOTE.2026-COMPENSATION` | v1 | Nota informativa sobre a compensação do art. 348, I e II: não calcula o valor compensado nem o efeito financeiro. Impacto financeiro não determinado. |
| `IBSCBS.NOTE.2026-WAIVER` | **v2** | Só dispara com avaliação de obrigações acessórias `Verified` no período e no escopo indicados. Compliance interno não equivale a regularidade jurídica universal; `RemediationVerified` comprova somente o escopo verificado e não produz dispensa. Quais documentos comprovam cada obrigação depende de revisão profissional. |

## Versões que continuam bloqueadas

| Regra | Versão | Estado | Por quê |
|---|---|---|---|
| `IBSCBS.NOTE.2026-WAIVER` | v1 | `Superseded` | Substituída pela v2; continua no catálogo só para reproduzir cálculos antigos |
| `CBS.RATE.REFERENCE-2027-2033` | v1 | `PendingRegulation` | Depende de resolução do Senado (art. 349) |
| `IBS.RATE.2027-2028-STATE` | v1 | `Unverified` | Aplicação ao Simples Nacional a partir de 2027 não verificada |
| `IBS.RATE.2027-2028-MUNICIPAL` | v1 | `Unverified` | Idem |

Esta decisão não ativa nenhuma delas. O sistema recusa aprovar para o piloto uma versão que não esteja `Active` (`tax_pilot_approval_status`). Operações de 2027 em diante são recusadas pela matriz de cobertura como `PendingRegulation`.

## Ressalvas que continuam obrigatórias

A aprovação interna **não elimina nenhuma ressalva**. Elas seguem nas telas, nos cálculos e nos dossiês:

- desconto incondicional exige efetiva redução do preço, registro no documento fiscal, ausência de evento posterior, evidência comercial e confirmação na execução;
- o documento fiscal sozinho não prova toda a realidade comercial;
- cálculo nominal não equivale a economia definitiva: o resultado é **redução do IBS/CBS calculado**, a diferença é **nominal na base de cálculo** e o **impacto financeiro não é determinado** sem demonstração suficiente;
- em 2026, cálculo, recolhimento e efeito financeiro são coisas distintas;
- compliance interno não equivale automaticamente a regularidade jurídica universal;
- `RemediationVerified` comprova somente o escopo efetivamente verificado e não produz dispensa, regularidade integral, extinção de penalidade nem reconhecimento fora desse escopo;
- créditos dependem dos requisitos legais aplicáveis. Enquanto não atendidos, a tela diz "Crédito potencial estimado";
- classificação fiscal não substitui a análise da operação;
- operações fora do recorte permanecem bloqueadas.

## Fontes jurídicas usadas pelo catálogo

Trechos literais da LC 214/2025, conferidos no texto compilado do Planalto em 26/10/2026 (`textVerification = Verified`; a conferência do texto não é parecer):

- art. 12, *caput* e § 1º (base de cálculo, acréscimos e descontos condicionais);
- art. 12, § 2º, III, e § 3º (desconto incondicional);
- art. 12, § 2º, V (ICMS, ISS, PIS e Cofins fora da base de 2026 a 2032);
- art. 343 (IBS de 2026);
- art. 346 (CBS de 2026);
- art. 348 (Simples Nacional, compensação e dispensa de 2026);
- art. 349 (alíquotas de referência, pendente);
- art. 344 (IBS de 2027–2028, não ativado).

Detalhes em [legal-sources.md](../legal-sources.md). Esta decisão não reescreve nenhum dispositivo legal. Se o código divergir da fonte documentada, corrige-se o código.

## Limitações

- O recorte é o da [matriz de cobertura](../supported-operations.md):
  - 2026;
  - fornecimento único e imediato;
  - regime regular ou Simples Nacional;
  - classificação suficiente;
  - sem regime diferenciado.
- Os códigos cClassTrib usados nos testes são fictícios. A V1 registra o código, mas não o interpreta.
- As evidências são referências externas com hash. Não existe upload privado.
- O split payment é expectativa gerencial e não comprova retenção ou recolhimento. Nenhum provedor está integrado.
- O hash do dossiê prova integridade, não veracidade material dos fatos.

## Accepted Risks for Controlled Pilot

| Risco | Classificação |
|---|---|
| A interpretação de cada regra não foi revista por profissional habilitado | **Requires Professional Review** (aceito para o piloto) |
| Códigos cClassTrib do regime geral não validados | **Requires Professional Review** |
| MEI/SIMEI e Simples acima do sublimite | **Requires Professional Review**. Fora do cálculo, fail-closed |
| Opções pelo regime regular dentro do Simples | **Requires Professional Review** |
| Documentos suficientes para cada obrigação acessória | **Requires Professional Review** |
| Efeito de `RemediationVerified` | **Requires Professional Review**. Hoje não satisfaz nenhuma regra |
| Texto da nota `IBSCBS.NOTE.2026-WAIVER` v2 | **Requires Professional Review** (aceito para o piloto com as ressalvas) |
| Requisitos e textos do detector de desconto incondicional | **Accepted for Pilot**, com as ressalvas acima |
| Exibição de cálculos nominais de 2026 a usuários do piloto | **Accepted for Pilot**. Sempre separados de recolhimento e de efeito financeiro |
| Uso do dossiê como registro interno auditável | **Accepted for Pilot**. Não é prova de conformidade fiscal |
| Valor de materialidade não configurado (regra desligada) | **Accepted for Pilot**. O valor é decisão de governança da empresa |
| Política de retenção documental não definida | **Accepted for Pilot**. Decisão da empresa, pendente |
| Evidência sem upload privado | **Accepted for Pilot**. Pendência técnica |
| Alíquotas e regras de 2027 em diante | **Pending Regulation** |
| Fornecimento continuado, antecipação, entrega futura, compra pública, regimes diferenciados e específicos | **Out of Scope** (fail-closed) |
| Integração com provedor de split payment | **Out of Scope** |

## Critérios para suspensão do piloto

O piloto, ou a aprovação de uma regra (`pilot-approval/withdraw`), deve ser suspenso quando:

1. um cálculo divergir da fonte jurídica documentada ou da reprodução histórica (`verify` não reproduzível);
2. uma operação fora do recorte for calculada em vez de recusada;
3. uma tela, um cálculo ou um dossiê apresentar a redução calculada como economia definitiva, ou o cumprimento interno como regularidade jurídica;
4. uma revisão profissional recebida pedir mudança (`ChangesRequired`) ou rejeitar (`Rejected`) uma regra do recorte;
5. uma nova norma ou regulamentação alterar o tratamento de 2026;
6. houver indício de acesso entre tenants, falha de segregação maker-checker ou perda de trilha de auditoria;
7. o Product Owner decidir encerrar ou rever o piloto.

Retirar a aprovação interna faz o sistema voltar a recusar os cálculos e as decisões que dependam da regra. O registro da aprovação e da retirada fica na auditoria.

## O que esta decisão não é

- Não é parecer profissional, homologação profissional nem validação externa.
- Não cria nem atribui nome, registro em conselho profissional, assinatura ou identificação profissional.
- Não é aprovação por ferramenta de IA.
- Não transforma `ProfessionalReview = Pending` em aprovação.
- Não amplia o escopo tributário além do que a v1 implementa.
- Não autoriza o modo `ProfessionalFinal`: para isso, cada regra precisa de revisão profissional efetivamente registrada.
