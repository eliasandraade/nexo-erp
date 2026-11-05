# SonarCloud: revalidação do `master` em 04/11/2026

Continuação da issue #84. Dados da API pública do SonarCloud (projeto `eliasandraade_nexo-erp`), com cada item conferido no código.

**Conclusão:** nenhuma vulnerabilidade real está aberta. Dos 183 "bugs", 8 são defeitos reais. As PRs #121, #122 e #123 corrigem 6 deles, mas o gate continua vermelho: o que falta depende do painel e da cobertura. A revisão também achou um defeito fora do SonarCloud, ainda sem correção: um laço de renderização em `PortalSetupPage` (seção 4).

## 1. Estado no commit analisado

**Commit:** `a8303f8` (HEAD do `master`), análise de 04/11/2026, 18:35 UTC.

**Quality Gate: ERROR**, com 4 de 6 condições falhando:

| Condição (código novo) | Valor | Limite | Situação | Causa |
|---|---|---|---|---|
| Confiabilidade | C | A | Falha | 56 bugs no período, nenhum CRITICAL |
| Segurança | C | A | Falha | 4 falsos positivos de fluxo (`S7044` ×3, `S5144`) |
| Hotspots revisados | 0% | 100% | Falha | 17 sem revisão; só o painel resolve |
| Cobertura | 72,1% | 80% | Falha | 16.356 de 56.724 linhas sem cobertura |
| Manutenibilidade | A | A | Passa | |
| Duplicação | 0,8% | 3% | Passa | |

| Métrica geral | Valor |
|---|---|
| Vulnerabilidades | 6 (4 no período) |
| Hotspots | 17 (`S2092` ×10, `S3330` ×6, `S5693` ×1) |
| Bugs | 183 (56 no período) |
| Cobertura geral | 61,9% |
| Code smells | 1.861, fora do gate (nota A) |

**O período de código novo não anda.** Ele está em `previous_version` desde 11/05/2026 e a versão do projeto é sempre "not provided". Por isso, "código novo" engloba quase seis meses de produto.

**As 4 vulnerabilidades de fluxo são efeito colateral da #110.** A contagem subiu de 5 para 6 justamente nesta análise. A #110 trocou `Regex.Replace(valor, @"\D", "")` por `string.Concat(valor.Where(char.IsAsciiDigit))` para resolver `S6444`. O caminho acusado passa por essas linhas, e o analisador não reconhece a versão com LINQ como sanitização. A proteção continua igual; só o indicador mudou. A data de criação na API (15/06) vem do histórico da linha do provedor, não do dia em que o achado surgiu.

## 2. Classificação

| Categoria | Vulnerabilidades | Hotspots | Bugs | Total |
|---|---:|---:|---:|---:|
| (a) Vulnerabilidade real | 0 | 0 | | **0** |
| (b) Falso positivo ou seguro no contexto | 6 | 17 | 151 | **174** |
| (d) Bug de confiabilidade real | | | 8 | **8** |
| (e) Manutenibilidade, sem defeito | | | 24 | **24** |
| Total | 6 | 17 | 183 | **206** |

(c) Cobertura não entra na soma: é a condição do gate, com 72,1% contra 80%.

### Vulnerabilidades

| Item | Local | Veredito | Motivo |
|---|---|---|---|
| `S7044` ×3, `S5144` ×1 | `OpenFoodFactsProvider:48`, `BrasilApiCepProvider:30`, `BrasilApiCnpjProvider:30`, `ViaCepProvider:30` | (b) | Os únicos chamadores são `LookupController` e `BarcodeController`. Eles mantêm só dígitos ASCII e exigem o tamanho exato (8, 14 ou de 8 a 14). `/`, `.`, `?`, `#` e `%` não chegam à URL, e o host vem de configuração, não da requisição |
| `S2068` | `CreateTenantDrawer.tsx:63` | (b) | É a mensagem de validação "Obrigatório" do campo `adminPassword` |
| `S6781` | `JwtTokenService.cs:192` | (b) | O valor de `appsettings.json` é público, e `JwtSecretPolicy.EnsureSafe` impede a API de subir com ele fora de Development/Testing. O segredo real vem de `Jwt__Secret` |

Duas ressalvas sobre `S7044` e `S5144`:

- **Alternativa ao painel:** `Uri.EscapeDataString` nos 4 provedores também tiraria os achados, como defesa em profundidade. Não foi feito porque não corrige um problema real.
- **Condição do veredito:** ele depende da lista de caracteres permitidos nos controllers. Qualquer novo chamador precisa validar da mesma forma.

### Hotspots

| Item | Local | Veredito | Motivo |
|---|---|---|---|
| `S2092` ×10, `S3330` ×6 | `AuthController.cs` | Seguro | `HttpOnly = true` sempre; `Secure = true` fora de Development, porque o TLS termina no proxy. O analisador não acompanha a cópia das opções nem um valor que não é constante |
| `S5693` | `StorageController.cs:91` | Seguro, com ressalva | Upload autenticado e restrito por papel. O limite do pedido é 20 MB, e o do arquivo é 10 MB (`MaxFileSizeMb`). A regra pede até 8 MB; reduzir é decisão de produto. O valor em produção não foi conferido nesta revisão |

### Bugs

| Regra | Qtd. | Local | Classe | Motivo |
|---|---:|---|---|---|
| `S9383` | 130 | `qc.invalidateQueries(...)` | (b) | Em `@tanstack/query-core` 5.104.3, `refetchQueries` aplica `.catch(noop)` quando `throwOnError` não está ligado, e nenhum desses pontos o liga: a promessa não rejeita. Não aguardar é intencional, para a mutação não ficar pendente durante o recarregamento |
| `S9383` | 6 | `invalidate()` em `useDeliveryMutations` (5), `refresh()` em `ListasDePrecoPage` (1) | (b) | Só repassam `invalidateQueries` |
| `S9383` | 4 | `handleUploadPhoto`, `handleLookup`, `lookupCep`, `attemptConnect` | (b) | Funções `async` com `try/catch` em todo o trabalho assíncrono |
| `S9383` | 1 | `verifyEmail(token).then` em `VerifyEmailPage` | (b) | O serviço captura tudo e devolve `{ success: false }` |
| `S9383` | 1 | `connection.stop()` em `useKitchenSocket` | (b) | O SignalR 8.0.29 captura as falhas de parada do transporte |
| `S9383` | 3 | `navigator.clipboard.writeText` | **(d)** | Rejeita de fato: clique sem retorno e rejeição não tratada. **#122** |
| `S3923` | 1 | `ProdutosPage.tsx:39` | **(d)** | "Todos" escondia os inativos, e "Inativo" filtrava só a página atual. **#123** |
| `S2871` | 2 | `PortalSetupPage.tsx:73`, `audit.api.ts:60` | **(d)** | Ordem errada na tela: "Água Fria" depois de "Washington Soares". **#121** |
| `S8733` | 1 | `BuildProjectRepository.cs:25` | **(d)** | Etapas × diários por obra: o resultado sai certo, mas o custo cresce com a duração da obra. Consulta só de leitura |
| `S1082` | 1 | `PlatformDashboardPage.tsx:215` | **(d)** a11y | Linha clicável sem foco nem teclado. "Ver todos" oferece outro caminho |
| `S8733` | 1 | `SaleRepository.cs:74` | (e) | Itens × pagamentos por venda, em lista paginada: custo limitado |
| `S8733` | 1 | `SaleRepository.cs:53` | (e) | Também usado em fluxos de escrita. O produto cartesiano é pequeno, e dividir a consulta troca uma leitura única por várias. Não aplicar a sugestão da regra sem analisar esses fluxos |
| `S8949` | 21 | `TenantResolutionMiddleware` (15), `SecurityStampValidationMiddleware` (6) | (e) | Cache, banco e resposta de erro sem `RequestAborted`. O pior caso é terminar uma consulta curta depois que o cliente saiu |
| `S1082` | 1 | `CreateTenantDrawer.tsx:91` | (e) | Clique no fundo é atalho; o botão de fechar funciona pelo teclado |
| `S8949` | 1 | `ExceptionHandlingMiddleware.cs:134` | (b) | Com o token do pedido abortado, a escrita do erro falharia dentro do próprio tratador |
| `S8949` | 2 | `AnalyzeMovementUseCase:165`, `ReprocessMovementUseCase:180` | (b) | Telemetria disparada sem aguardar, com escopo próprio e erros capturados. O token do pedido a cancelaria ao fim da resposta |
| `S8949` | 6 | `OrderService` | (b) | Notificações disparadas sem aguardar; o mesmo argumento vale. Arquivo em alteração paralela, sem mudança aqui |

## 3. O que as PRs corrigem

| PR | Corrige | Sai do SonarCloud | Efeito no gate |
|---|---|---|---|
| #123 | Filtro de status da lista de produtos, com novo parâmetro `onlyInactive` | `S3923` ×1 | 1 bug a menos no período |
| #122 | Falha ao copiar passa a ser avisada | `S9383` ×3 | 2 bugs a menos no período |
| #121 | Ordem alfabética em português | `S2871` ×2 | Nenhum: fora do período. A nota geral deve sair de D para C |

A #122 também corrige dois pontos com o mesmo defeito que o analisador não acusa: `ServicePortalPage` e `MfaSection`. Na `MfaSection`, a tela dizia "Códigos copiados." mesmo com a cópia recusada, e os códigos de recuperação só aparecem uma vez.

**Depois das três:** bugs de 183 para 177, e bugs no período de 56 para 53. **A condição de confiabilidade continua em C**, porque qualquer bug MAJOR no período mantém essa nota.

## 4. O que sobra, em ordem de prioridade

| # | Item | Classe | Ação sugerida |
|---:|---|---|---|
| 1 | Laço de renderização em `PortalSetupPage.tsx:569-583` (fora do SonarCloud; detalhes abaixo) | (d) | Trocar o `= []` da desestruturação por uma constante fora do componente, com teste |
| 2 | `S8733` em `BuildProjectRepository.GetByIdWithDetailsAsync` | (d) | `AsSplitQuery()`: consulta só de leitura, com teste de integração carregando as duas coleções na ordem certa |
| 3 | `S1082` em `PlatformDashboardPage.tsx:215` | (d) a11y | Transformar a linha em link ou botão |
| 4 | Cobertura de 72,1% para 80% | (c) | Definir antes o período (seção 5). Com o período atual faltam cerca de 5 mil linhas, em código antigo. Priorizar testes de fluxos críticos, sem criar teste só pelo percentual |
| 5 | `S8733` em `SaleRepository.cs:74` | (e) | Projetar os campos da lista em vez de carregar itens e pagamentos inteiros |
| 6 | `S8949` nos middlewares (21) | (e) | Repassar `RequestAborted` numa PR mecânica, depois do trabalho paralelo de acesso por tenant, para evitar conflito |
| 7 | `S1082` em `CreateTenantDrawer.tsx:91` | (e) | Fechar com Esc e foco preso no painel, se o componente for revisto |
| 8 | `S8733` em `SaleRepository.cs:53` | (e) | Manter como está; aceitar no painel se o custo não aparecer em produção |

### Laço de renderização em `PortalSetupPage`

Este defeito apareceu ao escrever o teste da #122: o teste nunca terminava e o processo estourou 4 GB de memória. O SonarCloud não o acusa, e ele não entra na contagem da seção 2.

`const { data: zonesData = [] }` cria um array novo a cada renderização enquanto a consulta de zonas não tem dados. O `useEffect` depende de `zonesData` e chama `setZoneFees` com um objeto novo, o que dispara outra renderização, e assim por diante.

| Situação da consulta de zonas | Renderizações medidas | Comportamento |
|---|---:|---|
| Responde logo | 31 no total | Normal |
| Demora 3 s e responde | 1.186 em 3 s; 1.188 ao final | O laço para quando a resposta chega |
| Falha | 1.107 em 3 s; 2.340 em 6 s | Não para |
| Sessão sem loja (consulta desligada) | 1.464 em 3 s; 2.875 em 6 s | Não para |

A medição foi feita em jsdom, com um teste descartável. O teste da #122 também contorna o laço, já começando com as zonas no cache. No navegador os números serão outros e o efeito não foi observado, mas a causa é a mesma: a tela deve consumir CPU continuamente se a consulta falhar. Uma busca pelo mesmo padrão no frontend (`data: x = []` como dependência de um efeito que altera estado) só encontrou este ponto; é uma busca por padrão de texto, não uma garantia.

Os 1.861 code smells não foram revisados um a um, mas os 18 BLOCKER são `S2699` (teste sem asserção) e merecem atenção. Os 9 de `AuthorizationTests.cs` não fazem chamada HTTP nem asserção, ou seja, passam sem verificar nada, apesar de nomes como `SuspendedTenant_Users_CannotAccessEndpoints` e `UserSecurityStamp_Changed_InvalidatesToken`. Não foi conferido se outros testes cobrem esses comportamentos, nem foram revisados os outros 9 achados da regra.

## 5. Ações que só o dono pode fazer no painel

### Falsos positivos e hotspots

| Ação | Itens | Efeito | Justificativa para colar |
|---|---|---|---|
| Marcar como **False Positive** | `S7044` ×3, `S5144` ×1 | Segurança no período sobe de C para A | "O controller só deixa passar dígitos ASCII com tamanho exato; o host vem de configuração." |
| Marcar como **False Positive** | `S2068` em `CreateTenantDrawer.tsx:63` | Nota geral de segurança | "Mensagem de validação do campo de senha, não uma senha." |
| Marcar como **False Positive** | `S6781` em `JwtTokenService.cs:192` | Nota geral de segurança | "O segredo vem de `Jwt__Secret`; `JwtSecretPolicy` recusa na partida o valor público." |
| Revisar como **Safe** | `S2092` ×10, `S3330` ×6 | Hotspots revisados | "`HttpOnly` sempre; `Secure` fora de Development, com TLS terminando no proxy." |
| Revisar como **Safe** ou mudar o limite | `S5693` em `StorageController.cs:91` | Hotspots revisados: 100% com os 17 | "Upload autenticado e restrito por papel; arquivo limitado a 10 MB." Confirmar `MaxFileSizeMb` em produção |
| Marcar em lote como **False Positive** | Os 142 `S9383` da seção 2 (todos menos os 3 da #122) | Reduz a confiabilidade no período | "A promessa não rejeita neste contexto." Use a mudança em lote por regra só depois do merge da #122 |
| Marcar como **False Positive** | `S8949` ×9 (`ExceptionHandlingMiddleware`, Interpreter, `OrderService`) | Nenhum sobre o período atual: todos são anteriores a ele | "Passar o token do pedido aqui causaria perda de trabalho." |

Marcar os `S9383` como falso positivo é a saída mais barata. A alternativa em código é prefixar `void` em 142 pontos, em PRs de até 30 ocorrências: não muda comportamento nem acrescenta proteção.

Mesmo com tudo isso, a confiabilidade no período só chega a A depois de resolver ou aceitar os 10 itens que restam nele: `S8949` ×9 nos middlewares e `S8733` em `SaleRepository.cs:74`.

### Período de código novo

Trocar `previous_version` em *Administration → New Code*. A #84 sugere começar no merge da #78 (26/10/2026). Se a versão por release for a opção escolhida, ela exige `sonar.projectVersion` no workflow, o que ficou fora desta rodada.

Pela data de criação na API, **nenhum dos 183 bugs, 6 vulnerabilidades e 17 hotspots é de 26/10 em diante**. Com essa data:

- As condições de confiabilidade, segurança e hotspots ficariam verdes no gate.
- Os itens antigos continuariam abertos nas notas gerais (segurança E, confiabilidade D). Trocar o período não corrige nada; só limita o gate ao código recente, e por isso os falsos positivos também devem ser marcados.
- A cobertura passa a medir só o código recente. O valor resultante não pode ser calculado antes da troca.

## 6. Observações

- **`docs/engineering/SONARCLOUD_COVERAGE.md` está defasado.** A seção "O que falta" e o comentário de `sonarcloud.yml` dizem que a análise pelo CI depende de ação do dono. O job do `a8303f8` terminou com "ANALYSIS SUCCESSFUL" e a cobertura já chega ao SonarCloud, então essa etapa parece concluída.
- **CNPJ alfanumérico (fora do escopo).** O filtro de `LookupController` só mantém dígitos, e não há tratamento de CNPJ alfanumérico no código. Se esse formato for suportado, a validação deve continuar como lista de caracteres permitidos (`0-9` e `A-Z`) para manter o veredito de `S7044`.
- **Limites desta revisão.** Não houve acesso a produção nem ao Railway: os valores reais de `MaxFileSizeMb` e `Jwt__Secret` não foram conferidos. Os números são da análise de `a8303f8`, antes do merge de qualquer PR desta rodada.

## 7. Atualização em 05/11/2026

Dados da API pública do SonarCloud, na análise do `master` em `650b397` (05/11, 18:37 UTC). Ela inclui #138, #140, #142, #143 e #145, desta rodada, e #141, feita em paralelo. Esta seção substitui a seção 5 no que diverge dela.

### 7.1 Estado do gate

**Quality Gate: ERROR**, com as mesmas 4 condições falhando. O gate não ficou verde, e nenhuma regra, limite, supressão ou exclusão foi alterada.

| Condição (código novo) | `a8303f8` | `650b397` | Limite | Situação |
|---|---|---|---|---|
| Confiabilidade | C | C | A | Falha |
| Segurança | C | C | A | Falha: resta só o `S5144` do ViaCEP no período |
| Hotspots revisados | 0% | 0% | 100% | Falha: só o painel resolve |
| Cobertura | 72,1% | 73,9% | 80% | Falha |
| Manutenibilidade | A | A | A | Passa |
| Duplicação | 0,8% | 0,8% | 3% | Passa |

| Métrica geral | `a8303f8` | `650b397` |
|---|---:|---:|
| Vulnerabilidades | 6 | 3 |
| Bugs | 183 | 168 |
| Bugs no período | 56 | 52 |
| Hotspots sem revisão | 17 | 17 |
| Cobertura geral | 61,9% | 64,8% |

As PRs abertas (7.2) não entram nesses números. Os 183 bugs do início caíram com #121, #122 e #123 (6) e com as PRs abaixo.

### 7.2 O que foi resolvido em código

| PR | Corrige | Na análise do `master` |
|---|---|---|
| #138 | `Uri.EscapeDataString` nos 4 provedores | `S7044` ×3 **saíram**. `S5144` no ViaCEP **continua** |
| #140 | `AsSplitQuery` no detalhe da obra; projeção na lista de vendas | `S8733` ×2 **saíram**. `SaleRepository.cs:54` continua, como previsto |
| #143 | Linha clicável vira link; painel de novo cliente com teclado e foco | `S1082` ×2 **saíram** |
| #145 | Documentação da análise pelo CI | Só texto |
| #141 (paralela) | Sessão encerrada ao mudar o papel; já repassa `RequestAborted` em `SecurityStampValidationMiddleware` | `S8949` nesse arquivo caiu de 6 para 1 |

PRs abertas quando esta seção foi escrita, cada uma com o gate da própria análise verde. O efeito delas no `master` só se confirma depois do merge:

| PR | Corrige | Análise da PR |
|---|---|---|
| #148 | `S2699` ×7: testes de `auth.e2e.spec.ts` reescritos e executados contra a pilha real | Gate OK, nenhum achado novo |
| #149 | `S8949` ×16: `TenantResolutionMiddleware` (15) e `SecurityStampValidationMiddleware` (1) | Gate OK, 86,4% de cobertura, nenhum achado novo |
| #151 | `S9383` ×5: as linhas cobertas por teste | Gate OK, nenhum achado novo |
| #152 | Endereços de CEP e CNPJ; deve resolver o `S5144` do ViaCEP | Gate OK, nenhum achado novo |

**Limite da evidência:** a análise de uma PR lista só os achados novos, e não mostra se um achado antigo foi embora. A #138 tinha 0 achados na PR e ainda assim o `S5144` continuou. Só a análise do `master` depois do merge confirma a saída de um achado.

### 7.3 Defeitos reais achados nesta rodada, fora da contagem do SonarCloud

| PR | Defeito | Como apareceu |
|---|---|---|
| #142 | `GET /api/sales/paged` com `status` ou `paymentMethod` respondia 500 em qualquer valor: os filtros da tela de vendas não funcionavam | Teste da lista, na #140 |
| #152 | Consulta de CEP e CNPJ pela API chamava endereço errado e recebia 404, tratado como "não encontrado", desde 15/06. Não conferido em produção | Investigação do `S5144` que continuou aberto |
| #149 | Cancelamento pelo cliente abria o circuito do Redis, que vale para o processo inteiro por 10 s. Nesse tempo, a renovação de sessão é recusada e o bloqueio de login por tentativas não vale. Já acontecia em janela menor, e repassar `RequestAborted` no middleware teria agravado | Análise antes de repassar `RequestAborted` |
| #148 | Sem rede, a tela de login mostrava "Failed to fetch" | Reescrita dos testes `S2699` |

### 7.4 O que sobra em código

| Item | Qtd. | Motivo |
|---|---:|---|
| `S9383` | 137 | Bloqueado pelo gate de cobertura: ver 7.5, decisão 2 |
| Testes de `auth.e2e.spec.ts` sem relação com `S2699` | 9 | Falham porque procuram `input[name="login"]`, que não existe mais. O arquivo não roda no CI. Sem issue aberta ainda |

### 7.5 O que depende do painel

As decisões são do dono do projeto. Em ordem de efeito no gate.

**1. Período de "código novo"** (*Administration → New Code*)

- **Hoje:** `previous_version` desde 11/05/2026. A versão do projeto é sempre "not provided", então o período nunca anda.
- **Sugestão:** data de referência 26/10/2026 (merge da #78).
- **Efeito:** a API não mostra nenhum bug nem vulnerabilidade criados de 26/10 em diante (`createdAfter=2026-10-26`; há 712 code smells, e a nota de manutenibilidade está em A). Os 17 hotspots têm data de 12/05 a 15/06. Confiabilidade, segurança e hotspots tendem a ficar verdes.
- **Ressalvas:** os itens antigos continuam abertos nas notas gerais. A cobertura passa a medir só o código recente, e esse valor só se conhece depois da troca.

**2. `S9383` nas 137 ocorrências restantes.** Escolher uma das opções:

- **A (recomendada): marcar em lote como False Positive.** Justificativa para colar:

  > A promessa não rejeita neste contexto. `invalidateQueries` passa por `refetchQueries`, que aplica `.catch(noop)` quando `throwOnError` não está ligado, e o projeto não usa `throwOnError`. As demais funções tratam a própria falha. Não aguardar é intencional.

- **B: aceitar as PRs de `void` com o gate vermelho.** Só o dono pode, porque `ci-gate` reprova. A #147 (Build ×38) está em rascunho e mostrou o motivo: 13,2% de cobertura no código novo. Prefixar `void` não muda comportamento, mas a linha alterada conta como nova, e só 5 das 142 ocorrências estão em linha executada por teste. Essas 5 estão na #151. As demais estão prontas, sem PR aberta: `fix/promessas-sem-rejeicao-comercial` (33), `fix/promessas-sem-rejeicao-restaurante-servico` (44), `fix/promessas-sem-rejeicao-catalogo-plataforma` (27).
- **C: escrever testes para esses `onSuccess`.** A regra do projeto proíbe teste só pelo percentual, e estes testes apenas repetiriam a lista de chaves do código. Não recomendado.

**3. Falsos positivos de vulnerabilidade**

| Item | Marcar como | Justificativa para colar |
|---|---|---|
| `S2068` em `CreateTenantDrawer.tsx:64` | False Positive | "Mensagem de validação do campo de senha, não uma senha." |
| `S6781` em `JwtTokenService.cs:192` | False Positive | "O segredo vem de `Jwt__Secret`; `JwtSecretPolicy` recusa na partida o valor público." |
| `S5144` em `ViaCepProvider.cs:30` | **Aguardar a #152.** Se continuar na análise do `master` depois dela: False Positive | "O controller só deixa passar 8 dígitos ASCII, o valor é escapado com `Uri.EscapeDataString` e o caminho tem prefixo fixo; o host vem de configuração." |

**Não** marcar `S7044` ×3: já saíram.

**4. Hotspots**

| Item | Marcar como | Justificativa para colar |
|---|---|---|
| `S2092` ×10 e `S3330` ×6 em `AuthController.cs` | Safe | "`HttpOnly` sempre; `Secure` fora de Development, com TLS terminando no proxy." |
| `S5693` em `StorageController.cs:91` | Safe, depois de confirmar `MaxFileSizeMb` em produção. A alternativa é reduzir o limite, decisão de produto | "Upload autenticado e restrito por papel; arquivo limitado a 10 MB." |

**5. Falsos positivos de confiabilidade.** Os 9 são anteriores ao período sugerido e não afetam o gate com a nova data.

| Item | Marcar como | Justificativa para colar |
|---|---|---|
| `S8949` em `ExceptionHandlingMiddleware`, `AnalyzeMovementUseCase`, `ReprocessMovementUseCase` e `OrderService` ×6 | False Positive | "Passar o token do pedido aqui causaria perda de trabalho: a escrita do erro ou a tarefa disparada sem aguardar seria cancelada ao fim da resposta." |
| `S8733` em `SaleRepository.cs:54` | Accept | "Também usado em fluxos de escrita; produto cartesiano pequeno, e dividir a consulta troca uma leitura única por várias." |

**Sem ação no painel:** `S8949` em `TenantResolutionMiddleware` (15) e `SecurityStampValidationMiddleware` (1), resolvidos em código pela #149 quando ela entrar.
