# ADR 0002 — Engine e forma canônica versionadas; texto conferido não é revisão profissional

- **Status:** proposta (PR de hardening empilhado sobre a #78; depende de revisão)
- **Data:** 27/10/2026

## Contexto

A v1 tinha uma engine e uma forma canônica estáticas. Um cálculo guardava `engine_version`, mas a verificação sempre rodava a engine atual: depois de qualquer mudança, cálculos antigos deixariam de ser reproduzidos (ou, pior, seriam "reproduzidos" por outra lógica). A migration inicial chamava código vivo para gerar o catálogo, então uma mudança futura nesse código mudaria o que a migration insere. "Verified" numa fonte podia ser lido como aprovação da regra.

## Decisão

1. `ITaxEngine` + `ITaxCanonicalizer`; `TaxEngineV1` e `TaxCanonicalV1` congeladas; `TaxEngineRegistry` com as versões da build. Cálculo novo usa a atual; leitura, re-detecção e verificação usam **exatamente** a versão gravada; versão ausente = "não pode reproduzir", nunca fallback.
2. A migration `TaxIntelligenceFoundation` insere o catálogo V1 como literais, sem referência a engine, catálogo, serializador ou domínio.
3. Fonte: `TextVerification` (o trecho reproduz o texto oficial). Versão de regra: `ProfessionalReview` (`Pending/Approved/ChangesRequired/Rejected`, com revisor, data e notas), independente do status técnico. Catálogo V1 inteiro `Pending`.
4. Política de produção: só regras com revisão `Approved` sustentam uma decisão; segregação de funções com justificativa para auto-aprovação.
5. Desconto incondicional: natureza `CalculatedTaxReduction`, fora das economias.

## Consequências

- Uma V2 é um arquivo novo, nunca uma edição da V1. A V1 fica registrada enquanto houver cálculo dela.
- Em produção, nada pode ser aprovado até a revisão profissional do catálogo — é o comportamento desejado.
- Golden hashes da V1 e a impressão digital do seed ficam fixados em testes.
- Nenhuma tese tributária foi criada ou alterada por esta decisão.
