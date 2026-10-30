# Runtime de IA — arquitetura e operação

## Fonte de verdade

```
SuperAdmin (/platform/ai/providers)
  → ai_providers (habilitado, padrão, prioridade, modelo, chave cifrada, limites, preços)
  → AiProviderRegistry (status real de cada provedor)
  → AnalyzerSelectorService (ordem de tentativa)
  → AnalysisRunner (execução, fallback, telemetria)
  → RuleBasedAnalyzer | ClaudeAnalyzer (SDK oficial da Anthropic)
```

O que o SuperAdmin grava é o que o runtime usa. As antigas chaves `Interpreter:Features:*` foram removidas: não existe mais
um segundo interruptor escondido em configuração.

**Único interruptor da implantação:** `Ai:Providers:{Provider}:Allowed` (padrão `true`). Ele só **retira** um provedor
(emergência); nunca liga um que esteja desligado no SuperAdmin. Regra: um provedor roda quando está **habilitado no
SuperAdmin E permitido pela implantação**. Na tela aparece como "Bloqueado na implantação".

## Status de um provedor

| Status | Quando |
|---|---|
| `available` | habilitado, implementado, permitido, com chave e modelo (quando exigidos) e sem teste reprovado recente |
| `disabled` | desligado no SuperAdmin |
| `blocked_by_deployment` | `Ai:Providers:{Provider}:Allowed=false` |
| `implementation_missing` | não há analisador para ele nesta versão (OpenAI hoje) |
| `missing_key` / `missing_model` | falta chave ou modelo |
| `secret_unavailable` | a chave não pode ser aberta aqui (sem `Secrets:MasterKey`, chave mestra trocada sem a antiga, ou formato legado ainda não migrado) |
| `invalid_credentials` | o último "Testar conexão" teve a chave recusada; fica até trocar a chave ou um novo teste passar |
| `rate_limited` / `unhealthy` | o último teste foi limitado ou falhou; vale por 15 min |

## Seleção e fallback

- **Automático:** provedores `available`, o padrão primeiro e depois por prioridade (menor número primeiro). Uma falha
  esperada (credencial, limite, timeout, rede, 5xx, modelo inexistente, resposta inválida, recusa) passa para o próximo e
  a resposta informa `fallbackUsed` e as tentativas. O leitor de regras (sem custo) é um provedor como os outros: se
  estiver habilitado, entra na ordem; desligá-lo o tira da ordem.
- **Forçado** (Playground, reprocessamento): só aquele provedor. Indisponível → `422 ai_provider_unavailable`; falhando →
  `503 ai_provider_failed` com o motivo em `details`. Nunca é trocado por outro em silêncio.
- **Nada disponível** → `422 ai_provider_unavailable`. Nenhuma dessas situações gera 500 nem entra em `error_events`.

## Chave de API

- Entra só pelo `PUT /api/platform/interpreter/providers/{id}/api-key` (super_admin), é cifrada na hora e não volta mais:
  a API devolve só `hasApiKey` e os 4 últimos caracteres.
- **AES-256-GCM**, nonce aleatório por cifragem, tag de 16 bytes, dados associados = `ai-provider/{id}/api-key` (um envelope
  copiado para outra linha não abre). Envelope: `s1.{keyId}.{base64(nonce‖tag‖cifra)}`.
- **Chave mestra fora do banco:** `Secrets:MasterKey` (variável `Secrets__MasterKey` no Railway), base64 de 32 bytes
  aleatórios. Sem ela nada é gravado e nada é lido (nenhuma chave de reserva).
- **Rotação da chave mestra:** gere uma nova, coloque-a em `Secrets__MasterKey` e a antiga em `Secrets__PreviousMasterKeys`
  (lista separada por vírgula); no próximo boot o serviço `AiKeyUpgradeHostedService` re-cifra tudo; depois remova a antiga.
- **Formato legado:** chaves gravadas antes desta versão (Base64 simples) são detectadas e re-cifradas no boot, em memória,
  sem log e sem precisar redigitar — desde que `Secrets:MasterKey` exista.
- Gerar uma chave mestra com um gerador criptográfico: `openssl rand -base64 32` (ou, no PowerShell 7,
  `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`). Nunca a coloque em arquivo
  versionado.

## Claude

- SDK oficial `Anthropic` (NuGet), cliente por chamada com o HttpClient nomeado `anthropic`, URL base fixa
  `https://api.anthropic.com` (uma `ANTHROPIC_BASE_URL` no ambiente não redireciona a chave), timeout de 30 s, 1 nova
  tentativa do próprio SDK.
- Saída estruturada (`output_config.format` com JSON Schema) e validação campo a campo: valor ≥ 0 e < 1 bilhão,
  arredondado a centavos; data AAAA-MM-DD entre 2000 e 2100; textos sem caracteres de controle e com até 200 caracteres;
  confiança limitada a 0–1. O que não passa vira "desconhecido" com confiança 0; um JSON inválido vira
  `invalid_response`. Só os valores validados são guardados — nunca a resposta bruta da Anthropic.
- O texto do usuário vai delimitado como dado (`<lancamento>…</lancamento>`); a resposta é sempre uma **sugestão**: o
  movimento continua rascunho até uma pessoa confirmar.
- Instruções = prompt `extraction` ativo no SuperAdmin (ou o embutido `builtin-1`); a versão e o hash (SHA-256 do conteúdo
  enviado) entram na telemetria.

## Testar conexão

`POST /api/platform/interpreter/providers/{id}/test` (super_admin): uma chamada mínima (16 tokens, 15 s, sem nova
tentativa), só quando clicado. Grava status, mensagem saneada e horário; nunca a resposta.

## Telemetria

Cada chamada registra provedor, **modelo**, operação, empresa, duração, tokens, custo estimado (preço por milhão de tokens
cadastrado no SuperAdmin), sucesso, fallback e erro saneado. Não registra chave, cabeçalho de autorização, prompt nem
resposta bruta.
