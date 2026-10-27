# ORKEN Tax Intelligence

Domínio tributário próprio do ORKEN. Os módulos operacionais (vendas, compras, serviços, financeiro) fornecem **fatos**; o Tax Intelligence responde **qual é o tratamento tributário** daquela operação, para aquele estabelecimento, naquela data, com as regras vigentes — e guarda tudo o que é preciso para reconstruir essa resposta anos depois.

| Documento | Conteúdo |
|---|---|
| [architecture.md](architecture.md) | Camadas, entidades, fluxo, multi-tenancy, permissões, segurança |
| [rule-engine.md](rule-engine.md) | Vocabulário das regras, avaliação, versionamento temporal, como criar e testar uma regra |
| [audit-model.md](audit-model.md) | Hashes, reprodução de cálculo histórico, trilha de auditoria, dossiê de decisão |
| [legal-sources.md](legal-sources.md) | Fontes jurídicas usadas, o que foi conferido e como adicionar uma fonte |
| [supported-operations.md](supported-operations.md) | Matriz de operações suportadas pela V1 (fail closed) |
| [roadmap.md](roadmap.md) | Limitações atuais e próximos passos |
| [adr-0001-versioned-isolated-tax-rules.md](adr-0001-versioned-isolated-tax-rules.md) | Decisão: regras tributárias versionadas e isoladas dos domínios operacionais |
| [adr-0002-engine-versioning-and-review.md](adr-0002-engine-versioning-and-review.md) | Decisão: engine e forma canônica versionadas; texto conferido ≠ revisão profissional |

## O que existe na v1

- **Perfil tributário versionado por estabelecimento** (regime, UF, município, CNAEs, regimes especiais, benefícios, obrigações acessórias). Mudar de regime cria uma nova versão; nada é apagado.
- **Catálogo de regras versionado** (plataforma), com fontes jurídicas literais conferidas no texto oficial. Uma versão ativa nunca é editada.
- **Engine determinística e versionada** (`orken-tax-engine/1.0.0`, forma canônica `orken-canonical/1`, congeladas): mesmo input + mesmas versões de regra = mesmo resultado, byte a byte. Cada cálculo guarda input, hashes, versões usadas e o rastro de cada regra.
- **Cálculo oficial idempotente e verificável** (`POST /api/tax/calculations/{id}/verify` reexecuta **com a engine exata do cálculo** e compara o hash; se ela não estiver disponível, diz que não pode reproduzir).
- **Revisão profissional separada da conferência de texto.** As fontes têm o texto conferido. Cada versão de regra tem a sua revisão tributária, com escopo, validade, premissas e ressalvas; todas estão `Pending` no catálogo V1.
- **Recorte explícito (fail closed).** Operação fora do que a V1 modela não é calculada: o sistema recusa e diz por quê.
- **Obrigações acessórias por período e escopo**, com estados de regularização e PNCT. Só a verificação conta para as regras.
- **Cenário e execução separados.** A aprovação do cenário não declara documento futuro; a execução é verificada depois, com o documento fiscal.
- **Créditos em ciclo de vida.** Um crédito potencial não é elegível, apropriado nem disponível.
- **Simulação** de cenários (regime, data, linhas, descontos, hipóteses de alíquota, crédito) que **não grava nada oficial**.
- **Oportunidades** com risco, confiança, requisitos, evidências, guardrails contra estruturas proibidas e workflow Identificada → Em análise → Aprovada/Rejeitada/Bloqueada. Nada é aplicado automaticamente.
- **Dossiê de decisão** imutável na aprovação.
- **Credit Hunter (fundação):** ledger de créditos IBS/CBS só de lançamentos, segregado por tributo.
- **Split payment (fundação):** retenção esperada a partir do cálculo, registro manual da retenção real e conciliação. Nenhum provedor integrado.
- **Telas** em `/tributos` (visão geral, perfil, cálculos, simulações, oportunidades, créditos, regras e fontes).

## Primeiro vertical slice

**Desconto condicional × incondicional no IBS/CBS de 2026** (LC 214/2025, art. 12, § 1º, III; § 2º, III; § 3º; arts. 343, 346 e 348). Uma proposta com desconto sob condição é calculada; o detector propõe concedê-lo sem condição e destacado na nota; a simulação mostra a diferença; os requisitos legais viram checklist; a resposta "o desconto ainda depende de evento posterior" bloqueia a oportunidade como divergência entre documento e operação; a aprovação (Diretoria) grava o dossiê. Detalhes em [architecture.md](architecture.md#vertical-slice).

## Aviso

O catálogo inicial reproduz literalmente dispositivos da LC 214/2025 conferidos no texto compilado do Planalto em 26/10/2026. **Isso não é parecer jurídico.** Nenhuma regra está marcada como aprovada por profissional. Antes de uso em produção, um profissional tributário deve revisar o catálogo, os requisitos do detector e os textos das telas — ver [roadmap.md](roadmap.md#o-que-depende-do-profissional-tributário).
