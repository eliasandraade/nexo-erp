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
| Atos Conjuntos RFB/CGIBS | índice oficial: <https://www.cgibs.gov.br/atos-conjuntos> (também <https://normas.receita.fazenda.gov.br/>) | ver a tabela abaixo |
| Atos Técnicos Conjuntos RFB/CGIBS | índice oficial: <https://www.cgibs.gov.br/atos-tecnicos-conjuntos> | ver a tabela abaixo |
| Resoluções do CGSN pertinentes a 2026 | Resolução CGSN nº 186/2026 (prazos e opção pelo regime regular do IBS/CBS para 2027) e nº 194/2026 (altera esses prazos) — consulta em <https://normas.receita.fazenda.gov.br/>; notícias oficiais: <https://www.gov.br/receitafederal/pt-br/assuntos/noticias/2026/abril/cgsn-define-prazos-de-opcao-pelo-simples-nacional-e-pelo-regime-regular-do-ibs-e-da-cbs-para-2027> e <https://www.gov.br/receitafederal/pt-br/assuntos/noticias/2026/agosto/cgsn-atualiza-regras-do-simples-nacional-para-adequacao-a-reforma-tributaria-do-consumo> | não lidas; a V1 recusa MEI/SIMEI e Simples acima do sublimite (`RequiresProfessionalReview`) até a leitura profissional; **a revisar profissionalmente** |

### Atos Conjuntos RFB/CGIBS (metadados conferidos no índice oficial em 27/10/2026)

| Ato | Data | Publicação informada no índice | Arquivo oficial | Situação no Orken |
|---|---|---|---|---|
| nº 1 | 22/12/2025 | DOU de 23/12/2025 | <https://www.cgibs.gov.br/upload/arquivos/202609/09104120-ato-conjunto-rfb-cgibs-n-1-de-22-de-dezembro-de-2025.pdf> | não lido; possivelmente relevante às obrigações acessórias de 2026 — **a revisar profissionalmente** |
| nº 2 | 27/05/2026 | site do CGIBS em 03/06/2026 | <https://www.cgibs.gov.br/upload/arquivos/202606/03111600-ato-conjunto-rfb-e-cibs-2827-05-29-assinado.pdf> | não lido (documentação técnica do split payment, segundo a notícia oficial). Manual e Swagger **não** são transação liquidada: o split segue gerencial |
| nº 3 | 19/06/2026 | site do CGIBS em 22/06/2026 | <https://www.cgibs.gov.br/upload/arquivos/202606/22083423-ato-conjunto-nro-3-dere.pdf> | não lido — **a revisar profissionalmente** |
| nº 4 | 30/07/2026 | site do CGIBS em 31/07/2026 | <https://www.cgibs.gov.br/upload/arquivos/202607/31091735-20260730-16h30-ato-conjunto-rfb-cgibs-na-c2-ba-4-260731-090909.pdf> | não lido — **a revisar profissionalmente** |
| nº 5 | 12/08/2026 | site do CGIBS em 13/08/2026 | <https://www.cgibs.gov.br/upload/arquivos/202608/13065932-ato-conjunto-rfb-cgibs-n-c2-ba-5-2026-regulamenta-o-programa-nacional-de-conformidade-tributaria-docx-assinado.pdf> | **vinculado ao modelo de compliance como fonte relevante** (PNCT): a participação é registrada na avaliação das obrigações acessórias, nunca equivale a regularidade integral e não gera benefício automático. Conteúdo não lido — **a revisar profissionalmente** |
| nº 6 | 28/08/2026 | site do CGIBS em 28/08/2026 | <https://www.cgibs.gov.br/upload/arquivos/202608/28193542-ade-rfb-cgibs-n-6-28-8-2026-com-paragrafo-unico-assinado.pdf> | não lido — **a revisar profissionalmente** |

### Atos Técnicos Conjuntos RFB/CGIBS (índice oficial em 27/10/2026)

O índice <https://www.cgibs.gov.br/atos-tecnicos-conjuntos> lista os nºs 1 a 9 de 2026:

- nº 1, de 31/07;
- nº 2, de 21/08;
- nº 3, de 02/09;
- nº 4, de 28/08;
- nº 5, de 22/09;
- nº 6, de 28/09;
- nº 7, de 28/09;
- nº 8, de 29/09;
- nº 9, de 20/10.

São especificações técnicas, como documentos fiscais eletrônicos e declarações. Nenhum foi lido nem usado por regra. Os de documentos fiscais tendem a importar para saber quais evidências comprovam a execução, mas essa avaliação cabe ao profissional (**a revisar profissionalmente**).

Nenhuma fonte inventariada virou regra `Active`.

Conferir sempre a versão vigente no Diário Oficial da União antes de usar qualquer uma.

## Afirmações rejeitadas por conflito com a fonte oficial

A revisão recebida trazia três afirmações que não foram implementadas. Testes de regressão (`TaxReviewDeltaTests`) impedem que voltem:

1. **"IBS de 2026 compensa ICMS/ISS."** Rejeitada. O art. 348, I e II, trata da compensação do valor recolhido de IBS/CBS em 2026 com as contribuições do art. 195, I, *b*, e IV, e com o PIS do art. 239 — ou, sem débitos suficientes, com outro tributo federal ou por ressarcimento. A nota `IBSCBS.NOTE.2026-COMPENSATION` segue o texto literal. Nenhum texto do módulo diz que IBS/CBS de 2026 compensa ICMS ou ISS.
2. **"cClassInt."** Rejeitada. O código de classificação do IBS/CBS usado no domínio é o **cClassTrib**; `cClassInt` não existe no código.
3. **"Art. 48 disciplina de forma genérica o uso ou consumo pessoal."** Rejeitada. O trecho conferido do art. 47 remete ao **art. 57** para o uso ou consumo pessoal. Nenhuma regra ou texto do módulo atribui essa disciplina ao art. 48, e nenhuma regra de crédito foi criada sem a leitura dos dispositivos oficiais.

## Como adicionar uma fonte

1. Copiar o trecho **literal** do site oficial (planalto.gov.br, gov.br/receitafederal, portal do Comitê Gestor, diários oficiais). Nada de resumo.
2. `POST /api/platform/tax/legal-sources` com `code` estável (`LEI-ANO-ARTX-PY-INC`), `kind`, `title`, `reference`, `url` (https), `excerpt`.
3. Outra pessoa confere o texto com a fonte e chama `POST /api/platform/tax/legal-sources/{code}/verify`.
4. Só então regras que a citam podem ser ativadas.
5. Nova redação da mesma norma = **nova fonte** com outro código; a antiga continua, porque cálculos e decisões antigas a citam.
