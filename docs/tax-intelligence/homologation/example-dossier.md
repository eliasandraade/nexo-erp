# Dossiê de exemplo — operação dentro do recorte da V1

> **Dados de teste.** Gerado pelo próprio sistema no teste de integração `TaxHomologationDossierTests`, sobre dados controlados. Empresa, CNPJ (11.222.333/0001-81), pedido, aditivo, NF-e, protocolo e conferência de obrigações são **fictícios**. O exemplo prova o fluxo; não afirma fato jurídico. Foi executado **como o piloto controlado roda**: módulo ligado, modo `ControlledPilot` configurado explicitamente e política de aprovação ligada. Antes do fluxo, o operador de teste da plataforma fez três ações explícitas e auditadas: abriu o piloto, admitiu a empresa e aprovou as versões de 2026. Nenhuma migration aprova nada. As regras têm **aprovação interna para piloto controlado**, que é decisão de produto e governança e **não é parecer profissional**. A **revisão profissional de todas continua pendente**. O hash demonstra a integridade do registro, não a veracidade material dos fatos. O dossiê é o registro auditável da decisão e das premissas utilizadas pelo motor, não prova definitiva de conformidade fiscal.
>
> O dossiê completo, exatamente como o sistema o produziu, está em [example-dossier.json](example-dossier.json). Para gerar de novo: rode o teste com `TAX_HOMOLOGATION_OUT=<arquivo>`. Ids, datas de registro e hashes mudam a cada execução; os valores calculados não.

## A operação

| Item | Valor |
|---|---|
| Contribuinte | Empresa de Teste Homologação Ltda — CNPJ 11222333000181 — SP / 3550308 |
| Estabelecimento | Perfil tributário v1: Lucro Presumido, SP, município 3550308, CNAE 4781400 |
| Contraparte | Lucro Real, SP |
| Operação | Venda (saída) de 15/06/2026, pedido PED-TESTE-001 |
| Datas | Fornecimento único e imediato. Operação, documento e fornecimento em 15/06/2026; sem pagamento antecipado nem período de execução |
| Item | Camiseta de algodão: mercadoria, NCM 61091000, cClassTrib `TEST01` (código **de teste**; o código real cabe ao profissional), sem regime diferenciado |
| Valores | Bruto R$ 10.000,00; desconto **condicional** R$ 500,00; ICMS/PIS/Cofins no preço R$ 900,00 |
| Cobertura | `Supported` (`orken-coverage/1`) |
| Obrigações acessórias | Avaliação `Verified` de 01/06 a 30/06/2026. Escopo: "Estabelecimento de teste — NF-e de saída do período". Conferido por "Contabilidade de teste"; limitação: dados de teste |

## O cálculo do sistema

| Item | Valor |
|---|---|
| Engine | `orken-tax-engine/1.0.0` com forma canônica `orken-canonical/1` |
| Hashes | Input, conjunto de regras e output estão no JSON (`calculation.*Hash`) |
| Regras aplicadas | `IBSCBS.BASE.SURCHARGES`, `IBSCBS.BASE.CONDITIONAL-DISCOUNT`, `IBSCBS.BASE.UNCONDITIONAL-DISCOUNT` e `IBSCBS.BASE.TRANSITION-TAXES` (v1); `CBS.RATE.2026-TEST` e `IBS.RATE.2026-TEST-STATE` (v1); `IBSCBS.NOTE.2026-COMPENSATION` (v1); `IBSCBS.NOTE.2026-WAIVER` (**v2**) |
| Fontes | LC 214/2025, trechos com o texto oficial conferido (`textVerification = Verified`) |
| Aprovação interna (piloto) | Todas as regras acima: **`ApprovedForControlledPilot`** (`orken-pilot-approval/1`), registrada pelo usuário autenticado da plataforma de teste, com as ressalvas e a referência da decisão de cada uma |
| Revisão profissional | Todas as regras acima estão **`Pending`** |
| Base / IBS+CBS calculado | R$ 9.100,00 / R$ 91,00 |

A nota da dispensa de 2026 (v2) aparece porque há uma avaliação `Verified` cobrindo a data.

## Governança registrada

O cálculo e os dois dossiês carregam a seção `governance`:

| Campo | Valor |
|---|---|
| `mode` | `ControlledPilot` |
| `policy` | `orken-pilot-approval/1` |
| `enforced` | `true` (a política de aprovação estava ligada) |
| `nature` | `INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT` |
| `effects` | "Piloto controlado — resultados para análise e validação. Não executa alteração fiscal, contábil ou financeira automaticamente." |
| `pilotScope` | decisão que abriu o piloto, janela e quem ativou |
| `notice` | "Decisão interna de produto e governança da Andrade Systems para o piloto controlado. Não constitui parecer jurídico, contábil ou tributário profissional." |
| `decisionReferences` | todas as decisões distintas por trás das regras. Aqui é uma só: [internal-pilot-approval-2026.md](internal-pilot-approval-2026.md) |
| `rules[]` | por regra: `technicalStatus`; `internalApproval` completa (status, política, usuário, capacidade, hora, escopo, validade, ressalvas e a **referência exata da decisão daquela versão**); `professionalReview = Pending` |
| `professionalReviewPending` | todas as regras usadas |

As duas dimensões aparecem lado a lado e uma nunca substitui a outra.

## Cenários

| | Cenário original (desconto condicional) | Cenário proposto (desconto incondicional no documento) |
|---|---|---|
| Base | R$ 9.100,00 | R$ 8.600,00 |
| IBS/CBS calculado | R$ 91,00 | R$ 86,00 |

- **Diferença de base:** R$ 500,00.
- **Diferença de IBS/CBS calculado:** R$ 5,00.
- **Natureza:** `CalculatedTaxReduction`, isto é, redução simulada do IBS/CBS calculado.
- **Efeito financeiro:** **não determinado**. Depende do período (compensação ou dispensa em 2026), dos preços praticados e de revisão profissional.
- **Ressalvas do detector:** a condição comercial muda; é redução do tributo calculado, não economia definitiva; em 2026 a redução aparece no valor destacado, não no caixa do período.

## Fluxo registrado

1. **Análise** (gerente de teste). Requisitos do cenário respondidos com notas:
   - instrumento comercial;
   - condição removida identificada (pagamento antecipado);
   - nenhum evento posterior;
   - nenhuma recobrança.

   A evidência comercial `ADITIVO-TESTE-001` (`CommercialInstrument`) foi vinculada ao requisito do instrumento.
2. **Aprovação do cenário** pela diretoria, que não participou da análise.
   - Maker-checker exigido para risco baixo: auto-aprovação com justificativa. Não foi usada: `selfApproval = false`.
   - O dossiê registra o documento fiscal como `pending-execution-verification` e a execução como `pending`.
3. **Depois da operação.**
   - Evidência `NFE-TESTE-0001` (`FiscalDocument`) vinculada ao requisito "consta do documento fiscal autorizado".
   - Requisitos da execução confirmados.
4. **Verificação da execução**: resultado `Verified`, protocolo `PROTOCOLO-TESTE-0001`. O dossiê da execução carrega o hash do dossiê da decisão.

Hashes dos dossiês nesta geração (os mesmos do JSON):

- decisão: `43ef260eb74b14dde0c9e9a4c93fdeab1970d3469b394637b7cece2e128c34b0`;
- execução: `d81d6f0a52b50c487e0d5826562b99072fe19543c3c5f56963a26f5dc6ad0502`.

## Pendências registradas no dossiê da decisão

- Verificação da execução pendente (no momento da aprovação).
- Revisão tributária pendente de todas as regras usadas (a aprovação interna para o piloto não a substitui).
