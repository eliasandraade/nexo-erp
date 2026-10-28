# ORKEN Tax Intelligence v1 — Internal Controlled Pilot Approval

> **INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT** — não é **PROFESSIONAL TAX OPINION**.
>
> Este registro documenta uma decisão interna de produto e governança da Andrade Systems. **Não constitui parecer jurídico, contábil ou tributário profissional.** Nenhum advogado, contador, parecerista ou consultoria emitiu opinião sobre estas regras, e nenhuma ferramenta de IA aprovou nada. A revisão profissional de todas as regras continua **pendente**.

| Campo | Valor |
|---|---|
| Produto | ORKEN Tax Intelligence v1 |
| Data da decisão | 27/10/2026 |
| Escopo | Vertical slice IBS/CBS 2026: o recorte suportado pela v1 (matriz `orken-coverage/1`, engine `orken-tax-engine/1.0.0`, forma canônica `orken-canonical/1`) |
| Decisão | **Approved for Controlled Pilot**: autoriza o uso, no piloto controlado, das versões listadas abaixo |
| Política | `orken-pilot-approval/1` |
| Responsável pela decisão | Product Owner / Andrade Systems |
| Quem registra no sistema | O usuário autenticado da plataforma que executa cada ação (id, nome exibido, capacidade e hora do servidor), nunca um nome digitado |
| ProfessionalReview | **Pending** para todas as regras |
| Natureza | Decisão interna de produto e governança |
| Aplicação | Exclusivamente ao recorte documentado e aos participantes admitidos. Operações não suportadas continuam fail-closed; períodos com regulação pendente continuam bloqueados |

## Esta decisão não se efetiva sozinha

A existência deste documento, um deploy ou uma migration **não autorizam nada**. A migration `TaxControlledPilotGovernance` só cria o schema:

- todas as versões começam `InternalApproval = NotApproved`;
- o escopo do piloto começa vazio;
- o módulo começa desligado.

Depois de qualquer deploy, o estado é:

```
TAX INTELLIGENCE:        DISABLED (Enabled=false)
APPROVAL MODE:           Disabled
INTERNAL PILOT APPROVAL: NONE
PILOT SCOPE:             EMPTY
```

Para o piloto existir, um operador da plataforma precisa fazer, explicitamente e nesta ordem:

1. Ligar o módulo e escolher o modo na configuração da instalação: `TaxIntelligence:Policy:Enabled=true` e `TaxIntelligence:Policy:Mode=ControlledPilot`. Sem as duas, nada roda.
2. Abrir a janela do piloto (`PUT /api/platform/tax/pilot`) com validade e `decisionReference` = este documento.
3. Admitir cada empresa participante, ou só algumas lojas dela (`POST /api/platform/tax/pilot/participants`). Nenhuma empresa participa por padrão.
4. Aprovar, uma a uma, as versões abaixo (`POST /api/platform/tax/rule-versions/{id}/pilot-approval`), com escopo, validade, ressalvas e `decisionReference`.

Cada ação fica na auditoria com o usuário autenticado e a hora do servidor. Só então o piloto libera cálculos e decisões para os participantes.

## Estado alvo, depois da ativação explícita

```
INTERNAL PILOT APPROVAL: APPROVED (por ação explícita, versão a versão)
PROFESSIONAL REVIEW:     PENDING
CONTROLLED PILOT:        ENABLED (configuração explícita + escopo de participantes)
UNSUPPORTED OPERATIONS:  FAIL CLOSED
PENDING REGULATION:      BLOCKED
```

## Como o sistema aplica a decisão

- **Duas dimensões separadas em cada versão de regra.**
  - `InternalApproval` (`NotApproved` / `ApprovedForControlledPilot` / `Withdrawn`): política, usuário autenticado que registrou (id, nome, capacidade), hora do servidor, escopo, validade, ressalvas e a referência da decisão.
  - `ProfessionalReview`: segue `Pending`. Nenhum revisor, registro ou assinatura foi criado.

  As duas coexistem.
- **O gate falha fechado.** Para um cálculo, uma simulação, uma detecção, uma decisão ou uma verificação de execução, tudo isto precisa ser verdade:
  1. módulo ligado (`Enabled`);
  2. modo configurado (`ControlledPilot`). Nenhum modo é usado como padrão;
  3. empresa e loja admitidas no escopo, dentro da validade do piloto;
  4. cada regra tecnicamente `Active` **agora**;
  5. cada regra com aprovação interna válida para a data, não retirada. Numa simulação isso vale para a data base e para a data de cada cenário. Numa nova detecção sobre um cálculo, vale para as regras que ele executou;
  6. operação `Supported` na matriz de cobertura;
  7. para uma decisão, o cálculo de origem ainda reproduzível com a engine e as regras que usou.

  Se qualquer uma falhar, a operação é recusada com 422 e a tentativa fica na auditoria (`tax_calculation_refused` / `tax_simulation_refused` / `tax_detection_refused` / `tax_decision_refused`).
- **Histórico preservado ≠ autorização atual.**
  - Uma versão revogada ou substituída mantém a aprovação registrada, mas não autoriza nada novo.
  - Um participante retirado, uma aprovação retirada, uma janela vencida ou o módulo desligado bloqueiam trabalho novo.
  - O que já foi registrado continua legível e reproduzível (`verify`).
- **Modo de produção definitiva** (`ProfessionalFinal`): ignora a aprovação interna e só aceita revisão profissional registrada.
- **Registro de cada execução.** Cada cálculo grava, fora do input e do output com hash:
  - modo, política e se a política estava ligada;
  - o escopo do piloto (decisão, janela, quem ativou);
  - por regra: status técnico, aprovação interna completa, com a referência **exata** da decisão daquela versão, e revisão profissional.

  Os dossiês congelam o mesmo e listam todas as decisões distintas (`decisionReferences`). Uma reaprovação posterior não reescreve um dossiê antigo.

## Piloto controlado = modo sombra

O piloto controlado é um modo de avaliação. **Não executa alteração fiscal, contábil ou financeira automaticamente.**

| Permitido no piloto | Nunca feito automaticamente |
|---|---|
| cálculos, simulações, detectores, oportunidades, workflow interno, dossiês, verificação de execução, comparação, auditoria | alterar venda ou preço; emitir ou alterar documento fiscal; lançar obrigação tributária oficial; apropriar ou utilizar crédito; enviar declaração; comandar recolhimento; liquidar split com provedor |

O módulo tributário não depende de nenhum adaptador de efeito fiscal, e o provedor de split só expõe status de configuração. Um teste de arquitetura garante essas duas coisas. Um teste de integração roda o fluxo do piloto e confirma:

- que o adaptador de documento fiscal não é chamado;
- que nenhum lançamento de crédito nem liquidação de split é criado.

As telas mostram: "Piloto controlado — resultados para análise e validação. Não executa alteração fiscal, contábil ou financeira automaticamente."

## Versões aprovadas pela decisão (a registrar uma a uma, por ação explícita)

Validade a registrar: 01/01/2026 a 31/12/2026. Ela não vai além da vigência de cada versão e cobre só 2026, mesmo quando a regra vale por mais tempo. As ressalvas abaixo são as que devem acompanhar cada aprovação.

| Regra | Versão | Ressalvas aceitas |
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

**Automáticos** (o gate recusa sozinho, sem depender de ninguém):

| Critério | Efeito |
|---|---|
| versão revogada, substituída ou não ativa | não autoriza cálculo nem decisão novos (`tax_pilot_approval_missing`) |
| aprovação interna retirada (`pilot-approval/withdraw`) | idem |
| aprovação interna fora da validade | idem |
| janela do piloto vencida ou piloto inativo | `tax_pilot_scope_expired` / `tax_pilot_scope_disabled` |
| empresa ou loja fora do escopo, ou retirada dele | `tax_pilot_scope_tenant` / `tax_pilot_scope_store` |
| módulo desligado (kill switch) | `tax_intelligence_disabled` |
| modo não configurado | `tax_approval_mode_disabled` |
| operação fora da cobertura | `tax_operation_not_supported` |
| cálculo de origem não reproduzível (verificação da engine quebrada) | nenhuma decisão nova (`tax_calculation_not_reproducible`) |

**Manuais** (dependem de julgamento; ao ocorrer, retirar a aprovação da regra, retirar o participante, fechar a janela ou desligar o módulo):

1. um cálculo diverge da fonte jurídica documentada;
2. uma tela, um cálculo ou um dossiê apresenta a redução calculada como economia definitiva, ou o cumprimento interno como regularidade jurídica;
3. uma revisão profissional recebida pede mudança (`ChangesRequired`) ou rejeita (`Rejected`) uma regra do recorte;
4. uma nova norma ou regulamentação altera o tratamento de 2026;
5. indício de acesso entre tenants, falha de segregação maker-checker ou perda de trilha de auditoria;
6. o Product Owner decide encerrar ou rever o piloto.

O registro das aprovações, retiradas e mudanças de escopo fica na auditoria.

## O que esta decisão não é

- Não é parecer profissional, homologação profissional nem validação externa.
- Não cria nem atribui nome, registro em conselho profissional, assinatura ou identificação profissional.
- Não é aprovação por ferramenta de IA.
- Não transforma `ProfessionalReview = Pending` em aprovação.
- Não se efetiva por migration, seed ou pela existência deste arquivo: só por ações explícitas e auditadas da plataforma.
- Não amplia o escopo tributário além do que a v1 implementa.
- Não autoriza o modo `ProfessionalFinal`: para isso, cada regra precisa de revisão profissional efetivamente registrada.
