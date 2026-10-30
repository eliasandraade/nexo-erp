# Minha Obra: frontend público integrado ao Orken Build (contrato v1.6)

O portal do cliente (`/{companySlug}/{projectSlug}`, ex.: `www.orken.com.br/solutie/shoppingeusebio`) fala só com a API
pública do Orken Build descrita em `docs/build/client-portal-contract.md` (branch `feat/build-complete`, PR #90). Não há
contrato provisório, gateway "indisponível" nem dados simulados em produção: os fixtures de `dev/Review.tsx` só existem em
DEV com `VITE_BUILD_PORTAL_REVIEW=1`.

## Arquitetura

```
API pública (OpenAPI v1.6) → api/generated/portal-types.ts → api/http-gateway.ts + api/adapter.ts
                           → types/index.ts (modelo de apresentação) → componentes
```

- `api/generated/portal-types.ts` é cópia literal de `docs/build/portal-types.ts`, gerado da API em execução
  (`BuildPortalOpenApiTests`). Atualize copiando o arquivo; não edite à mão.
- `api/http-gateway.ts` é o único lugar que conhece rotas, headers e códigos. Componentes não veem DTOs nem tokens.
- `credentials: "omit"`, nunca `Authorization`; sessão só em memória, no header `X-Build-Portal-Session`.
- `PortalPage.tsx` injeta `createHttpGateway()`; trocar de slug destrói a sessão anterior.

## Comportamento por resposta

| Resposta | O que o portal faz |
|---|---|
| 401 `portal_password_required` | tela de senha (sem dados da obra) |
| 401 `portal_password_invalid` | "Não foi possível acessar com essa senha." |
| 401 sessão expirada/revogada | volta à senha, descarta os dados carregados |
| 404 | "Não encontramos este acompanhamento." — igual para empresa ou obra inexistente |
| 409 | mensagem do servidor + "O registro foi atualizado com a situação atual."; recarrega o estado real |
| 413 / 422 | mensagem do servidor junto ao formulário; nada é mostrado como enviado |
| 429 + `Retry-After` | contagem regressiva; nenhuma requisição até o fim; formulário de senha oculto |
| seção falhou | só aquela seção mostra o erro, com "Tentar carregar novamente"; as outras seguem |

Ações (escolha, aditivo, aceite, chamado, fotos, avaliação) só atualizam a tela depois da resposta de sucesso, recarregando o
estado do servidor. O resultado aparece junto do formulário que o enviou. Uploads mostram "Enviando foto N de M…".

## URLs assinadas

- Nunca são guardadas como identificador. O portal recarrega os dados 30 s antes da primeira expirar, no máximo a cada
  14 min e no mínimo a cada 1 min (`refreshDelay`), e também ao voltar o foco para a janela.
- Relógio do aparelho adiantado em relação ao servidor não gera loop: links que parecem já vencidos mantêm a janela de
  14 min. Expiração da sessão adiantada pelo mesmo motivo é decidida só pelo servidor (401).
- Imagem ou vídeo que falha (link vencido) renova os links uma vez, sem a pessoa perceber (`media.ts`), no máximo uma
  vez por minuto. Link de documento vencido é renovado em vez de aberto ("Toque novamente para abrir").
- Datas de garantia (`expiresAt`) não são confundidas com expiração de link.

## Gêmeo digital

- O GLB é baixado uma vez. Datas usam `GET /twin/state?date=` com `If-None-Match`; a resposta 304 reaproveita o estado.
- Etapa, categoria e pavimento de cada elemento vêm de `elementIndex` (v1.6): vínculos reais da construtora, classe IFC
  gravada e zona. Nada é deduzido de nome de nó. Sem índice, os filtros não aparecem.
- Pavimento = zona `Storey` acima do elemento; em modelos sem pavimentos IFC, a zona do elemento.
- Classes IFC conhecidas têm nome em português ("Paredes", "Lajes e pisos"…); as demais aparecem como gravadas.
- Sem WebGL: aviso e lista textual de elementos e estados.

## Diferenças contrato/UI que permanecem (regras reais do backend)

- Escolha e aditivo são registros separados: a escolha fica "Enviada, aguardando a construtora". Quando muda preço ou
  prazo, a construtora gera um aditivo, que o cliente decide.
- O aditivo gerado de uma escolha traz o prazo de fornecimento na descrição e "Impacto no prazo: Sem alteração". O backend
  não converte o prazo de fornecimento em atraso de obra.
- Nome é obrigatório no aceite e na decisão. Nome só com espaços é recusado pelo servidor (400) e mostrado no formulário.
- Assistência aceita só fotos (JPEG, PNG, WebP; até 8 MB; até 6 por chamado), conferidas pelo conteúdo no servidor.
- Valor de aditivo e de medição só aparece com `financialSummary` ligado; sem ele, o portal não mostra valor.
- Hotspots 360° e nuvem de pontos: o contrato não publica coordenadas. Nuvem de pontos é só download.
- O histórico do chamado mostra os status registrados; agendamento aparece como "Atendimento: data", não como etapa.

## Testes

- Unidade: `npx vitest run src/modules/build-portal` (gateway, adapter, mídia, índice do gêmeo, relógio, upload).
- Visual com fixtures (DEV): `npx playwright test --config playwright.build-portal.config.ts`.
- Integração real (API real, CORS real, PostgreSQL descartável, Docker):

  ```bash
  BUILD_BACKEND_ROOT=<checkout de feat/build-complete>/nexo-backend npx playwright test --config playwright.build-portal.integration.config.ts
  ```

  O Playwright sobe `e2e/backend-host` (controllers, serviços e política CORS reais sobre Testcontainers, sem nenhuma
  rota simulada da API) e o Vite em outra origem. A única rota interceptada é o armazenamento por trás das URLs
  assinadas (`storage.test`): ela serve os bytes que a API gravou e recusa assinaturas revogadas, como um link S3
  vencido.

Cobertura da suíte de integração (19 cenários):
- portal público (390, 430 e 1440 px) e privado;
- senha errada, bloqueio com `Retry-After`;
- sessão revogada, sessão expirada no servidor, sessão expirada no navegador;
- financeiro oculto, linha do tempo, fotos, documentos;
- renovação de URL por timer e por falha de imagem;
- aditivo: nome exigido, impacto de prazo, conflito 409;
- escolha → confirmação → aditivo gerado;
- aceite de entrega;
- assistência: tipos permitidos, 413 e 422, evidência, protocolo, histórico;
- avaliação única;
- gêmeo digital: GLB real, modos, filtros do índice, corte, seleção, datas, ETag e 304, GLB baixado uma vez;
- sem WebGL;
- captura de realidade;
- isolamento entre empresas e obras.

## Direção visual

Cliente abre o link no WhatsApp, em um celular sob luz ambiente, procurando tranquilidade sobre a obra. Superfície clara,
tipografia legível e fotografia real em destaque; nome/identidade da construtora à frente, Orken só no rodapé. Hierarquia
narrativa: progresso e previsão, publicação da semana, etapas, modelo, atualizações, imagens, escolhas, documentos,
cuidado pós-entrega. Ausência de fotos não gera fotografia falsa.

## Viewer: escolha técnica

Three.js direto, em chunk lazy carregado só após ação do usuário. O backend processa IFC; o navegador não infere
vínculos. Renderização sob demanda, DPR limitado, descarte no unmount, fallback de erro/perda de contexto. Busca de
elementos por mapa (modelos com milhares de nós).

## Pendências antes de publicar

- Medir modelos grandes (milhares de elementos, Draco/Meshopt) e GPU móvel em aparelho físico.
- Validar em iPhone/Android físicos e com leitor de tela falado; medir Core Web Vitals em produção.
- O site público precisa estar em `Cors:AllowedOrigins` se não for `www.orken.com.br` ou `app.orken.com.br` (já fixos).
