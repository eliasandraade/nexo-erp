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

"Verified" significa que o trecho confere com o texto oficial — **não é parecer jurídico**.

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

## Não revisado

O cabeçalho do texto compilado cita o **Decreto nº 12.955, de 2026**. Ele **não** foi lido nem usado; nenhuma regra depende dele. Antes de ampliar o catálogo, conferir esse decreto e os atos da Receita Federal e do Comitê Gestor do IBS.

## Como adicionar uma fonte

1. Copiar o trecho **literal** do site oficial (planalto.gov.br, gov.br/receitafederal, portal do Comitê Gestor, diários oficiais). Nada de resumo.
2. `POST /api/platform/tax/legal-sources` com `code` estável (`LEI-ANO-ARTX-PY-INC`), `kind`, `title`, `reference`, `url` (https), `excerpt`.
3. Outra pessoa confere o texto com a fonte e chama `POST /api/platform/tax/legal-sources/{code}/verify`.
4. Só então regras que a citam podem ser ativadas.
5. Nova redação da mesma norma = **nova fonte** com outro código; a antiga continua, porque cálculos e decisões antigas a citam.
