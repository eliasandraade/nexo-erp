# Quando a sessão acaba

O que encerra as sessões de uma pessoa: mudar o papel, inativar ou bloquear, trocar a senha (a própria, a redefinida pelo gerente,
pela plataforma ou pelo link de e-mail), ligar a verificação em duas etapas, "sair de todos os dispositivos" e o encerramento pela
plataforma. Todos trocam o carimbo (`SecurityStamp`) no banco. Empresa suspensa ou cancelada não troca carimbo, mas perde o acesso
do mesmo jeito.

## Empresa suspensa ou cancelada

Toda chamada dos usuários, a negociação do hub incluída, responde `403` com `code: "tenant_suspended"`. O campo `error` continua com o
texto de sempre. A sessão não acaba: reativada a empresa, o mesmo token volta a valer.

A tela da cozinha trata esse código (`useKitchenScreen`): mostra o aviso, desliga o hub, troca o polling de 10 s por uma verificação por
minuto e volta sozinha quando o acesso retorna. As verificações param antes de o token expirar, porque `POST /auth/refresh` recusa a
renovação de uma empresa fora de atividade e o cliente derruba a sessão quando a renovação é recusada. Depois disso, a tela fica parada com
o aviso e quem está nela pede a verificação. Se o acesso já voltou, a renovação funciona e a tela retoma. Se a empresa continua suspensa, o
caminho é o de qualquer sessão que não renova: a tela de entrada.

## Requisições HTTP

`SecurityStampValidationMiddleware` compara o carimbo do token com o banco a cada requisição, sem cache (issue #137): a resposta da
mudança é o ponto de corte. Isso inclui a negociação e a abertura de uma conexão do hub.

## Conexões do hub já abertas

Uma conexão não faz mais requisições, então a mesma regra é aplicada a ela de outras quatro formas. A regra está em
`HubSessionGuard`: carimbo igual ao do banco, pessoa ativa, empresa ativa. Quem falha é fechado sem direito a reconectar, e o cliente
não precisa colaborar: depois de fechada, o servidor não escreve mais nada naquela conexão.

| Caminho | Quando | Prazo |
|---|---|---|
| Aviso depois do commit (`HubSessionChangeInterceptor`) | qualquer gravação que troque o carimbo, a situação da pessoa ou a da empresa, em qualquer código | antes da resposta da mudança, nesta instância |
| Redis pub/sub (`HubSessionSignals`) | a mesma gravação, para as outras instâncias | na hora, se o Redis estiver no ar |
| Varredura (`HubSessionSweepJob`) | a cada 20 s, em uma leitura só para todas as conexões da instância | até 30 s (intervalo + limite de 10 s da passada), mesmo com o aviso perdido ou o Redis fora do ar |
| A cada chamada do hub (`HubSessionFilter`) | `JoinStore`, `LeaveStore` e qualquer método futuro | na hora: recusa e fecha |

- O aviso é só uma dica: quem recebe consulta o banco. Um aviso atrasado ou repetido não derruba uma conexão nova, de um token novo.
- O banco fora do ar não fecha ninguém. A varredura tenta de novo na passada seguinte e, enquanto isso, uma chamada do hub é recusada
  sem fechar a conexão: nada entra num grupo sem conferir a sessão. Sem banco, o prazo de 30 s fica suspenso, e o HTTP também não responde.
- Sem nenhuma conexão aberta, a varredura não toca no banco.
- Sair de um acesso da plataforma (impersonação) fecha as conexões daquele token. Esse fim não está no banco, e sim no cache, como no
  caminho HTTP: se o aviso se perder com o Redis fora do ar, vale a expiração do token (até 30 min).

## Expiração do token

O hub fecha a conexão quando o token dela expira (`CloseOnAuthenticationExpiration`, a cada 15 min no máximo), dizendo que o cliente
pode voltar. O cliente (`useKitchenSocket`) renova o token antes de reconectar. Se a renovação for recusada, a sessão acabou e ele
para. Sem veredito do servidor (sem conexão, 429, 5xx), a sessão continua, e ele tenta um número limitado de vezes e cai no polling.

## Renovação

Só uma recusa do servidor encerra a sessão: 401, ou 409 depois da espera pela aba que ganhou a corrida. Sem conexão, 429 e 5xx, a
sessão fica, a chamada falha com um erro que a pessoa consegue ler, e nenhuma aba do navegador renova de novo antes do `Retry-After`
(ou, sem ele, de uma espera que dobra, até 1 min). `auth/refresh` e `platform/auth/refresh` têm uma política própria, separada do login,
pensada para muitos terminais atrás do mesmo endereço:

| Camada | Chave | Padrão (`RateLimiting:AuthRefresh:*`) | Para quê |
|---|---|---|---|
| Teto, em memória | endereço (`ClientIpResolver`) | `PermitLimit` 900 por `WindowSeconds` 900 | Inundação |
| Orçamento de falha, no Redis | endereço | `FailureBudget` 30 por `FailureWindowSeconds` 900 | Tentativa e erro. Só token inválido conta e só ele recebe 429 |
| Orçamento da sessão, no Redis | família do token | `FamilyBudget` 300 por `FamilyWindowSeconds` 900 | Cliente em laço. 429 para aquela sessão, que continua viva |

- Token vencido assinado por nós, corrida entre abas (409) e sessão encerrada não gastam o orçamento de falha. Na plataforma, o token é
  opaco: um token desconhecido faz o papel da assinatura inválida, e a família é a sessão.
- Um token já usado é julgado antes de qualquer orçamento: passar do limite nunca esconde uma reutilização.
- **Quem desiste depois de um 409 avisa o servidor.** Se ninguém no navegador recebeu o par novo, outra pessoa pode estar com ele. Antes de limpar a sessão, o cliente chama
  `auth/logout` com o mesmo token, sem esperar a resposta. Isso encerra a família mesmo dentro da carência e deixa um aviso na auditoria (`logout_with_spent_token`). Sem isso, a vítima nunca
  apresentaria o token de novo e a reutilização nunca seria detectada.
- **`auth/logout` aceita o refresh token sem access token,** porque quem o tem já poderia renová-lo. Tem teto próprio (`RateLimiting:AuthLogout:*`, 300 por 15 min por endereço; quem tem access conta à
  parte) e corpo de no máximo 16 KB. Sem nenhum dos dois tokens, continua 401.
- **Origem fora da lista:** uma página de qualquer site consegue mandar a estas rotas um POST simples, sem preflight, e os limitadores contariam esse pedido antes de o MVC recusar o tipo de conteúdo. Por isso,
  `api/auth/*` e `api/platform/auth/*` respondem `403 origin_not_allowed` a qualquer `Origin` fora da lista, antes dos limitadores. Sem `Origin` (quem não é navegador), segue como antes. O Swagger fora de
  produção é uma página: para usar essas rotas por ele, inclua a origem dele em `Cors:AllowedOrigins`.
- **Logout durante uma queda do Redis:** só o banco ouve. Quando o Redis volta do snapshot, a entrada do token volta válida; por isso a renovação também confere a linha da sessão (`IsRevokedAsync`), e
  a recusa marca a família. A sessão da plataforma mora no banco e não tem essa falha.
- `RateLimiting:AuthRefresh:Enabled=false` desliga as três camadas.
- **Redis fora do ar ou lento:** os orçamentos deixam de contar, sem abrir o disjuntor do cache, e o teto continua valendo. Como a
  validade do refresh de empresa está no Redis, a renovação responde 503 com `Retry-After` para a sessão que o banco conhece como ativa
  e 401 para a que ele sabe encerrada. A sessão da plataforma mora no banco e segue renovando.

## Token nos logs

Um navegador não manda cabeçalho em WebSocket, então o token do hub vai na query string. A linha de log com a query é do framework, e
`Microsoft: Warning` a filtra em `appsettings.json`. `AccessTokenLogRedactor` tira o token também se alguém subir o nível. Os logs do
proxy da hospedagem estão fora do alcance da aplicação.

## Mais de uma instância

Hoje a IaC define uma réplica (`.railway/railway.ts`). O fechamento já funciona com mais de uma, mas os eventos não: sem backplane do
SignalR, um evento só chega às conexões da instância que atendeu a requisição.
