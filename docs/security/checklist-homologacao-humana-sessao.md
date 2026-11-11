# Checklist da homologação humana: sessão em cookie

Para a validação manual do proprietário, antes da promoção da sessão em cookie para a produção. Ela resume os pontos do
[roteiro](roteiro-homologacao-sessao.md) que uma pessoa precisa ver com os próprios olhos. Os passos completos e onde olhar nas ferramentas do
navegador estão no roteiro.

**Uma etapa só fica aprovada quando uma pessoa a executou e viu o resultado.** O resultado de um teste automatizado não marca nenhuma linha
desta lista. As rodadas automatizadas estão no [registro](homologacao-sessao-staging-2026-11-10.md).

## Antes de começar

- **Endereços:**
  - app: <https://erp.staging.orken.com.br/login>;
  - plataforma: <https://erp.staging.orken.com.br/platform>;
  - API: `https://api.erp.staging.orken.com.br`. Ela só aparece nas ferramentas do navegador; não é para abrir.

  Os dois precisam abrir com cadeado válido antes de qualquer linha. Não use os endereços `*.up.railway.app`: neles o cookie atravessa
  sites diferentes, e Safari e Firefox podem recusá-lo.
- **Navegadores:** Chrome ou Edge, Firefox e Safari (macOS; iPhone se der), todos atualizados. Use uma janela anônima nova por fluxo, salvo
  quando o passo pede o mesmo navegador.
- **Ferramentas (F12):** em **Network**, marque **Preserve log**.
  - O cookie aparece em **Application > Cookies** (no Firefox, **Armazenamento > Cookies**; no Safari, **Armazenamento**), no host da **API**.
  - Ele se chama `__Host-nexo_rt` e precisa estar com HttpOnly, Secure e SameSite `Lax`.
- **Contas:** só as de teste do staging. As senhas não ficam neste arquivo.

  | Conta | Onde está a senha | Para quê |
  |---|---|---|
  | `lucas.mix` (Diretoria, Grupo Mix, 2 lojas) | Seeder do projeto: `nexo-backend/src/Nexo.Infrastructure/Persistence/Seed/DataSeeder.cs` | Login, abas, troca de loja, logout, sem conexão, migração, rollback |
  | Uma conta de teste com duas etapas | Ative em `/perfil` > verificação em duas etapas, com o seu aplicativo autenticador. Guarde os códigos de recuperação. Desative no fim | MFA (linhas 4 a 6) |
  | Administrador da plataforma | Railway > projeto `nexoerp` > ambiente `staging` > serviço `backend` > Variables: `Seed__PlatformEmail` e `Seed__PlatformPassword` | Impersonação e plataforma (linhas 11 e 12). A impersonação exige duas etapas: ative em `/platform/seguranca` e desative no fim |
  | Contas bloqueada, inativa e vendedor | Crie em `/usuarios` com a Diretoria, ou peça ao operador | Linhas 1 e 2 |

- **E-mail no staging:** o staging não envia e-mail, porque não tem `Resend:ApiKey`.
  - **Verificação (linha 14):** o link vai para o log. Railway > `staging` > `backend` > Logs, procure `VERIFICATION EMAIL`.
  - **Redefinição (linha 13):** o log registra só o assunto. Peça a redefinição pela tela, e o operador entrega o link da conta de teste
    na hora do teste.
  - **Recebimento real dos dois e-mails:** só dá para conferir onde houver um provedor configurado.
- **Em caso de falha:** anote o passo, o navegador, o horário e o `X-Request-Id` da resposta, e marque **Não**.

## Lista

Marque cada célula com **Sim** (fiz e vi o resultado), **Não** (falhou) ou **—** (não se aplica).

| # | Etapa | O que fazer | O que precisa acontecer | Chrome/Edge | Firefox | Safari |
|---|---|---|---|---|---|---|
| 1 | Login | Entrar como gerente e como vendedor | Cada um vai ao destino do seu papel. O cookie aparece no host da API com HttpOnly, Secure e Lax. O Local Storage fica sem `nexo:access_token` e sem `nexo:refresh_token` | | | |
| 2 | Login recusado | Senha errada; conta bloqueada; conta inativa | Cada caso mostra a sua mensagem, e nenhum cookie novo aparece | | | |
| 3 | Logout | "Sair da conta" | Vai ao login, o cookie some, e o botão Voltar não reabre o painel | | | |
| 4 | MFA: código | Entrar com a senha do gerente com duas etapas, errar o código, depois acertar | Nenhum cookie no passo da senha. "Código inválido." mantém o passo aberto. O código certo entra | | | |
| 5 | MFA: recuperação | Em outra janela, usar um código de recuperação, e repetir o mesmo código | Entra uma vez. Na segunda, "Código inválido." | | | |
| 6 | MFA: desafio expirado | Deixar a tela do código aberta e terminar o mesmo login em outra janela | A primeira janela mostra "A verificação expirou" e volta à senha | | | |
| 7 | Troca de loja | Com duas abas abertas, trocar de loja numa delas | A outra mostra a loja nova sem recarregar, e a loja nova continua depois de recarregar as duas | | | |
| 8 | Várias abas: recarregar | Abrir quatro abas no painel e recarregar todas ao mesmo tempo | Todas continuam logadas | | | |
| 9 | Várias abas: login | Deixar uma aba no login e entrar por outra | A primeira sai do login sozinha | | | |
| 10 | Várias abas: logout | Sair numa aba | As outras vão sozinhas ao login, com o aviso de sessão encerrada | | | |
| 11 | Impersonação | Como administrador da plataforma (com duas etapas), abrir uma empresa > "Entrar como cliente" > "Sair do acesso" | A aba nova tem a faixa do acesso, e nenhum pedido dela leva `Cookie`. "Sair do acesso" fecha a aba. A aba do administrador continua logada depois de recarregar | | | |
| 12 | Plataforma | Em Segurança, "Encerrar todas" | Vai ao login, e o cookie com `p.` some | | | |
| 13 | Redefinição de senha | Com uma janela já logada, pedir a redefinição pela tela "Esqueci minha senha", seguir o link do e-mail e criar a senha nova | O login mostra o aviso e aceita a senha nova. A janela antiga, ao recarregar, vai ao login com "Seu acesso foi alterado" | | | |
| 14 | Verificação de e-mail | Cadastrar uma empresa de teste, seguir o link, depois abrir o mesmo link de novo | Na primeira vez, entra sozinho. Na segunda, a tela mostra "Link expirado ou inválido" | | | |
| 15 | Migração de sessão | Ver a nota 1 abaixo | Continua logado sem novo login. O Local Storage perde os tokens, e o cookie aparece | | | |
| 16 | Sem conexão: navegar | Logado, colocar a rede em Offline e recarregar | A tela continua, com "Sua sessão continua ativa". Ao voltar a rede, a navegação segue sem novo login | | | |
| 17 | Sem conexão: sair | Em Offline, "Sair da conta". Voltar a rede, recarregar e entrar de novo na mesma página | Vai ao login. Ao voltar a rede, `auth/logout` sai sozinho com 204 e o cookie some. O novo login funciona | | | |
| 18 | Rollback sem perda indevida | Ver a nota 2 abaixo | Durante a volta, quem migrou vai ao login. A sessão no modo de hoje continua. Depois da restauração, todas voltam sem novo login | | | |
| 19 | Isolamento | No Console de outro subdomínio de `orken.com.br`, tentar gravar `__Host-nexo_rt` | O navegador recusa, e o cookie da API não muda | | | |

### Notas

1. **Migração de sessão (linha 15).** Precisa do operador.
   1. O operador publica o bundle sem o modo cookie.
   2. Você entra e não sai.
   3. O operador publica o bundle com o modo cookie.
   4. Você recarrega a mesma aba.
2. **Rollback (linha 18).** Precisa do operador.
   1. Antes, mantenha duas sessões abertas: uma no modo cookie e outra aberta durante a volta do bundle.
   2. O operador publica o bundle sem o modo (primeiro) e depois a API anterior. Recarregue as duas sessões entre um passo e outro.
   3. O operador restaura a API e depois o bundle. Recarregue as duas de novo.

## Resultado

| Item | Resposta |
|---|---|
| Todas as linhas com **Sim** nos três navegadores (ou **—** justificado) | |
| Falhas encontradas, com `X-Request-Id` | |
| Aprovo a promoção do commit candidato | Sim / Não |
| Nome | |
| Data e hora | |
