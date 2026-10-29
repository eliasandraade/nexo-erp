# ADR 0001 — Regras tributárias são versionadas e isoladas dos domínios operacionais

- **Status:** aceita (Tax Intelligence v1)
- **Data:** 26/10/2026

## Contexto

O ORKEN precisa calcular IBS e CBS, comparar alternativas e registrar decisões tributárias para milhares de empresas, e conseguir explicar, anos depois, como cada número foi obtido. A legislação muda (LC 214/2025, alterada pela LC 227/2026; alíquotas que dependem de resolução do Senado; regulamentos ainda por vir), e cada operação deve ser tributada pela regra vigente na data do fato gerador.

## Decisão

1. **Domínio próprio.** As regras vivem no Tax Intelligence. Vendas, compras, serviços e financeiro só fornecem fatos (`TaxOperationRequest`) e não contêm regra tributária.
2. **Regras como dados versionados.** Cada regra tem identidade estável e versões com vigência, fontes jurídicas, condições e resultado tipados e hash do conteúdo. Uma versão ativa nunca é editada; mudança = nova versão.
3. **Engine determinística e pura**, sem banco, relógio nem modelo de linguagem, com forma canônica e hashes de input, regras e output.
4. **Oportunidades são propostas.** Nada é aplicado automaticamente; há workflow com revisão, requisitos, guardrails e aprovação, e a aprovação só registra a decisão.

## Por quê

- **Não espalhar regras por Sales e cia.:** regra espalhada não tem vigência, fonte nem versão, e não dá para reconstruir o passado. Cada mudança de lei viraria mudança em vários módulos, sem trilha.
- **Não usar IA como calculadora:** o resultado precisa ser reproduzível, explicável e defensável numa fiscalização. Uma inferência não é reproduzível byte a byte nem tem fundamento verificável. IA pode ajudar a classificar ou explicar, mas o número oficial sai da engine.
- **Não sobrescrever regras históricas:** um cálculo de 2026 tem de poder ser refeito em 2031 com as regras de 2026. Por isso a versão é imutável, o cálculo guarda os ids e hashes das versões usadas e a verificação os recarrega, mesmo `Superseded`.
- **Não aplicar oportunidades automaticamente:** economia tributária mexe em preço, contrato e documento fiscal; pode cruzar a linha da simulação ou da divergência documental. A decisão é de pessoas, com risco declarado e dossiê. Diferimento não é economia, e o sistema nunca soma um ao outro.

## Consequências

- Mais tabelas e mais disciplina (toda regra precisa de fonte conferida).
- Recalcular com regras novas gera um **novo** cálculo; o antigo permanece.
- Mudar a semântica da engine exige nova versão e preservar a anterior para reprodução.
- O catálogo é da plataforma: tenants leem, não editam. Regras específicas de tenant ficam para depois, com o mesmo versionamento.
