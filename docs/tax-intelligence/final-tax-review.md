# ORKEN Tax Intelligence v1 — Homologação final

## Contexto

A revisão tributária anterior concluiu **"APTO APÓS AJUSTES"**. Os ajustes foram implementados, e esta homologação é sobre a **implementação corrigida**.

Não existe, no repositório, parecer profissional assinado. Nenhuma regra foi marcada como aprovada. Toda versão do catálogo continua `ProfessionalReview = Pending` até o seu registro.

- Exemplo real preenchido pelo sistema: [homologation/example-dossier.md](homologation/example-dossier.md); o JSON completo está em [homologation/example-dossier.json](homologation/example-dossier.json).
- Recorte aceito: [supported-operations.md](supported-operations.md).
- Fontes e afirmações rejeitadas: [legal-sources.md](legal-sources.md).

## O que mudou após a revisão

| Pedido da revisão | Implementação |
|---|---|
| Compliance com tempo, escopo e regularização | Avaliação histórica por período e escopo, com os estados `Unknown`, `Declared`, `Verified`, `InconsistencyIdentified`, `UnderRemediation`, `RemediationVerified`, `NonCompliant` e `NotApplicable`. Só `Verified` satisfaz regra. |
| PNCT | A participação é registrada, sem efeito de regularidade e sem benefício automático. Ato Conjunto RFB/CGIBS nº 5/2026 inventariado. |
| Revisão profissional expressiva | Estados `ReviewReceived` e `ApprovedWithCaveats`. Cada revisão guarda revisor, identificação, escopo, validade, premissas e ressalvas. |
| Fail closed | Matriz de cobertura: fora do recorte, nada é calculado e o motivo é registrado. |
| Datas e eventos | Domínio com padrão de fornecimento e datas de documento, fornecimento, pagamento e execução. Só fornecimento único e imediato é suportado. |
| Aprovação × execução | Aprovação do cenário e verificação da execução são registros separados; a segunda vem selada sobre a primeira. |
| Evidência real e vinculada | Tipo, requisito, referência, hash, status e notas. Upload de arquivo ainda não existe. |
| Requisitos do desconto | Cenário: instrumento comercial, condição removida, nenhum evento posterior, nenhuma recobrança. Execução: documento fiscal autorizado e preço final. |
| Créditos | Ciclo potencial → requisitos em revisão → a apropriar → apropriado/utilizado. |
| Classificação suficiente | Mercadoria: NCM + cClassTrib. Serviço: NBS + cClassTrib. Sem isso, `RequiresClassification`. |
| Simples / MEI | MEI/SIMEI e Simples acima do sublimite vão para `RequiresProfessionalReview`. |
| Split | Expectativa gerencial de segregação; não comprova retenção ou recolhimento. |
| Maker-checker | Por risco e materialidade. Justificativa não substitui independência. |
| Dossiê | Novos campos: contribuinte, estabelecimento, contraparte, datas, classificação, compliance, revisão, pendências, diferença de base e de tributo calculado, efeito financeiro "não determinado", aviso de integridade. |
| Retenção | Pendente de definição legal; nenhum descarte automático. |
| Nota de dispensa 2026 | Versão 2 só de texto. A versão 1 fica preservada, com status `Superseded`. |

Afirmações dos documentos recebidos que **não** foram implementadas, por conflito com a fonte oficial:

- "IBS de 2026 compensa ICMS/ISS";
- "cClassInt" — o código é cClassTrib;
- "art. 48 trata genericamente de uso ou consumo pessoal" — o art. 47 remete ao art. 57.

Detalhes em [legal-sources.md](legal-sources.md#afirmações-rejeitadas-por-conflito-com-a-fonte-oficial).

## Pontos para homologação

Pedimos confirmação expressa **somente** dos itens abaixo.

### 1. Detector de desconto incondicional
Confirmar:
- a definição (LC 214/2025, art. 12, § 1º, III; § 2º, III; § 3º);
- os requisitos do cenário e da execução;
- os fatos e documentos que bastam para cada requisito;
- a linguagem "redução do IBS/CBS calculado", que não é economia definitiva.

### 2. Tratamento de 2026
Confirmar:
- as alíquotas do recorte (CBS 0,9%; IBS estadual 0,1%);
- o tratamento de compensação e dispensa (art. 348);
- o texto da nota da dispensa v2;
- a distinção entre **cálculo**, **recolhimento** e **efeito econômico** usada nas telas e no dossiê.

### 3. Compliance / PNCT
Confirmar:
- os estados;
- o escopo;
- a temporalidade (período);
- as evidências suficientes para cada obrigação;
- o efeito de cada estado sobre as regras;
- o efeito jurídico de `RemediationVerified`.

### 4. Créditos
Confirmar:
- o ciclo de vida;
- a linguagem ("crédito potencial estimado…");
- os requisitos antes da apropriação;
- os estados potencial / a apropriar / apropriado / utilizado.

### 5. Matriz de cobertura
Confirmar o recorte que o sistema aceita ([supported-operations.md](supported-operations.md)) e o comportamento fail-closed. Em especial:
- quais códigos cClassTrib estão no regime geral;
- MEI/SIMEI;
- Simples acima do sublimite;
- a opção pelo regime regular.

### 6. Dossiê
Avaliar o dossiê real preenchido ([example-dossier.md](homologation/example-dossier.md)).

**Pergunta objetiva:** "Com os documentos e fatos registrados, o dossiê contém informação suficiente para sustentar a análise desta operação específica?"

### 7. Revisão profissional por regra
Para cada versão abaixo, registrar o resultado: `Approved`, `ApprovedWithCaveats`, `ChangesRequired` ou `Rejected`. Em cada uma, informar **escopo**, **vigência**, **premissas** e **ressalvas**.

| Regra | Versão | Status atual |
|---|---|---|
| `IBSCBS.BASE.SURCHARGES` | 1 | Pending |
| `IBSCBS.BASE.CONDITIONAL-DISCOUNT` | 1 | Pending |
| `IBSCBS.BASE.UNCONDITIONAL-DISCOUNT` | 1 | Pending |
| `IBSCBS.BASE.TRANSITION-TAXES` | 1 | Pending |
| `CBS.RATE.2026-TEST` | 1 | Pending |
| `IBS.RATE.2026-TEST-STATE` | 1 | Pending |
| `IBSCBS.NOTE.2026-SIMPLES` | 1 | Pending |
| `IBSCBS.NOTE.2026-COMPENSATION` | 1 | Pending |
| `IBSCBS.NOTE.2026-WAIVER` | 1 | Pending (`Superseded` — preservada para reprodução) |
| `IBSCBS.NOTE.2026-WAIVER` | 2 | Pending |
| `CBS.RATE.REFERENCE-2027-2033` | 1 | Pending (`PendingRegulation`, não aplicada) |
| `IBS.RATE.2027-2028-STATE` | 1 | Pending (`Unverified`, não aplicada) |
| `IBS.RATE.2027-2028-MUNICIPAL` | 1 | Pending (`Unverified`, não aplicada) |

Campos do registro de revisão: status, revisor, identificação profissional, escopo, vigência (de/até), premissas, ressalvas e notas. Ficam vazios até o profissional preencher.

## Pendências que continuam explícitas

Nenhuma destas vira padrão silencioso. Até nova manifestação profissional, o sistema recusa ou marca como pendente:

- quais códigos cClassTrib exatos estão no regime geral;
- MEI/SIMEI;
- Simples acima do sublimite;
- opções pelo regime regular do IBS/CBS;
- documentos suficientes para cada obrigação acessória;
- efeito jurídico de `RemediationVerified`;
- aprovação de `IBSCBS.NOTE.2026-WAIVER` v2;
- retenção documental;
- valor de materialidade (`TaxIntelligence:Policy:MaterialityThreshold`). É configurável e fica desligado se não for configurado; o valor é decisão da política de governança da empresa;
- upload privado de evidências. Hoje há só referência externa + hash.
