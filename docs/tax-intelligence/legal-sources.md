# Fontes jurídicas

## Regra

Nenhuma regra material entra sem fonte primária verificável. Conhecimento do modelo não vira regra. Quando a norma ainda depende de regulamentação, a regra é guardada como `PendingRegulation` e **não é aplicada**.

## Fontes do catálogo v1

Todas da **Lei Complementar nº 214, de 16 de janeiro de 2025**, copiadas literalmente do **texto compilado** no Planalto (que já traz as alterações da LC nº 227, de 2026), em 26/10/2026:
<https://www.planalto.gov.br/ccivil_03/leis/lcp/Lcp214compilado.htm>

| Código | Dispositivo | Usada por |
|---|---|---|
| `LC214-2025-ART10` | art. 10, caput — fato gerador no fornecimento | data de referência do cálculo (documentação) |
| `LC214-2025-ART12-CAPUT-P1` | art. 12, caput e § 1º — base = valor da operação; acréscimos e descontos sob condição integram | `IBSCBS.BASE.SURCHARGES`, `IBSCBS.BASE.CONDITIONAL-DISCOUNT`, detector |
| `LC214-2025-ART12-P2-III-P3` | art. 12, § 2º, III, e § 3º — descontos incondicionais fora da base; definição | `IBSCBS.BASE.UNCONDITIONAL-DISCOUNT`, detector, requisitos |
| `LC214-2025-ART12-P2-V` | art. 12, § 2º, V — ICMS, ISS, PIS/Pasep e Cofins fora da base de 2026 a 2032 | `IBSCBS.BASE.TRANSITION-TAXES` |
| `LC214-2025-ART31` | art. 31, caput — split payment | fundação do split (documentação) |
| `LC214-2025-ART47` | art. 47, caput e § 1º — crédito no regime regular, segregado IBS/CBS, documento eletrônico idôneo | invariantes do ledger de créditos |
| `LC214-2025-ART343` | art. 343 — IBS estadual 0,1% em 2026 | `IBS.RATE.2026-TEST-STATE` |
| `LC214-2025-ART344` | art. 344 — IBS 0,05% estadual + 0,05% municipal em 2027–2028 | `IBS.RATE.2027-2028-*` (**Unverified**, não aplicada) |
| `LC214-2025-ART346` | art. 346 — CBS 0,9% em 2026 | `CBS.RATE.2026-TEST` |
| `LC214-2025-ART348` | art. 348, I a III e §§ 1º e 2º — compensação, redução em regimes diferenciados, Simples fora, dispensa de recolhimento | alíquotas de 2026 e notas |
| `LC214-2025-ART349` | art. 349, I e II — alíquotas de referência por resolução do Senado | `CBS.RATE.REFERENCE-2027-2033` (**PendingRegulation**) |

`TextVerification = Verified` significa **só** que o trecho confere com o texto oficial — **não é parecer jurídico** e não aprova nenhuma regra. A interpretação de cada regra tem um estado separado, `ProfessionalReview`, e **todas as versões do catálogo V1 estão `Pending`**. Nenhuma foi marcada como aprovada.

## Decisões de interpretação (para revisão profissional)

| Ponto | Decisão na v1 | Por quê |
|---|---|---|
| MEI e "Simples acima do sublimite" | tratados como optantes do Simples Nacional (art. 348, III, c) | continuam optantes do Simples |
| Regimes diferenciados e específicos (art. 348, III, a e b) | itens marcados com `specialTreatment` não recebem alíquota; aviso na saída | a engine ainda não tem as reduções e bases específicas — melhor não calcular do que calcular errado |
| IBS de 2027–2028 (art. 344) | guardado como `Unverified` | a aplicação aos optantes do Simples a partir de 2027 não foi verificada |
| CBS de 2027–2033 | `PendingRegulation` | depende de resolução do Senado (art. 349, I) e da redução do art. 347 |
| Desconto do PDV | tratado como incondicional, com a ressalva de que precisa constar do documento fiscal | é dado no ato e não depende de evento posterior; o ORKEN ainda não emite NFC-e |
| Crédito em compras (art. 47) | só estimado em simulação quando o usuário assume (`assumeCreditable`) | elegibilidade em 2026 não regulamentada para o caso geral |
| Arredondamento | centavos, meio para longe do zero, por componente e linha | convenção de engenharia desta versão, não regra legal |

## Inventário de fontes oficiais — a revisar profissionalmente

Fontes primárias localizadas em sites oficiais em 27/10/2026. **Nenhuma delas foi lida por inteiro, interpretada ou usada por regra do catálogo**; nada abaixo é resumo de conteúdo. Ficam listadas para o profissional tributário decidir o que muda no catálogo. Fora destes endereços (blogs, resumos, material de curso) nada é fonte.

| Fonte | Endereço oficial | Situação |
|---|---|---|
| LC nº 214/2025 — texto compilado | <https://www.planalto.gov.br/ccivil_03/leis/lcp/Lcp214compilado.htm> | trechos do catálogo V1 conferidos literalmente em 26/10/2026; interpretação **a revisar profissionalmente** |
| LC nº 227, de 13/01/2026 | <https://www.planalto.gov.br/ccivil_03/leis/lcp/lcp227.htm> (republicação e retificação em <https://www.planalto.gov.br/ccivil_03/_ato2023-2026/2026/ret/lcp227-26-rep.doc> e <https://www.planalto.gov.br/ccivil_03/_ato2023-2026/2026/ret/lcp227-26-ret.doc>) | não lida; alterações na LC 214 só entram pelo texto compilado acima; **a revisar profissionalmente** |
| Decreto nº 12.955, de 29/04/2026 (regulamento da CBS) | <https://www.planalto.gov.br/ccivil_03/_ato2023-2026/2026/decreto/d12955.htm> | não lido; alterado pelo Decreto nº 13.075/2026 (<https://www.planalto.gov.br/ccivil_03/_ato2023-2026/2026/decreto/d13075.htm>); **a revisar profissionalmente** |
| Atos Conjuntos RFB/CGIBS de 2026 | consulta oficial: <https://normas.receita.fazenda.gov.br/> e <https://www.cgibs.gov.br/>; ex.: Ato Conjunto nº 4, de 30/07/2026 (<https://www.cgibs.gov.br/upload/arquivos/202607/31091735-20260730-16h30-ato-conjunto-rfb-cgibs-na-c2-ba-4-260731-090909.pdf>) | não lidos; **a revisar profissionalmente** — em especial o que diz respeito às obrigações acessórias de 2026 (condição do art. 348, § 1º) |
| Atos Técnicos Conjuntos | <https://www.cgibs.gov.br/atos-tecnicos-conjuntos> | não lidos; especificações técnicas (documentos fiscais, declarações); **a revisar profissionalmente** |

Conferir sempre a versão vigente no Diário Oficial da União antes de usar qualquer uma.

## Como adicionar uma fonte

1. Copiar o trecho **literal** do site oficial (planalto.gov.br, gov.br/receitafederal, portal do Comitê Gestor, diários oficiais). Nada de resumo.
2. `POST /api/platform/tax/legal-sources` com `code` estável (`LEI-ANO-ARTX-PY-INC`), `kind`, `title`, `reference`, `url` (https), `excerpt`.
3. Outra pessoa confere o texto com a fonte e chama `POST /api/platform/tax/legal-sources/{code}/verify`.
4. Só então regras que a citam podem ser ativadas.
5. Nova redação da mesma norma = **nova fonte** com outro código; a antiga continua, porque cálculos e decisões antigas a citam.
