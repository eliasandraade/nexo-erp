# Recuperação administrativa de empresa sem Diretoria ativa

**Situação: proposta. Nada aqui está implementado, e nenhum privilégio foi dado a ninguém.** O dono decide os pontos da seção 9 antes de
qualquer código.

## 1. O problema

Uma empresa sem nenhum usuário ativo na Diretoria não consegue se administrar: ninguém cria usuários, promove ou reativa alguém.

O sistema não deixa a empresa chegar a esse estado (`LastDirectorGuard`, PR #156), mas ela pode já estar nele. Exemplos: uma correção
manual no banco, dados anteriores à regra, ou um cadastro cuja única Diretoria nunca confirmou o e-mail.

Hoje só SQL resolve:

- A impersonação exige uma Diretoria ativa: sem ela, `POST /api/platform/tenants/{id}/impersonate` responde `400`.
- `PUT /api/users/{id}` é negado durante a impersonação (`DenyDuringImpersonation`).
- As rotas da plataforma sobre um usuário (`reset-password`, `force-logout`, `sessions`) não mudam quem é Diretoria.

SQL não deixa auditoria, não tem limite e depende de alguém com acesso ao banco de produção. É isso que esta proposta substitui.

## 2. Quem pode fazer

- **Só `super_admin`.** O papel `support` não faz. São os dois papéis de plataforma que existem (`PlatformUser.Role`).
- **Política `PlatformAdmin`**, que já exige sessão com verificação em duas etapas, mais **um código TOTP novo no próprio ato**, porque a
  sessão pode ter horas. A exigência vale mesmo com `Platform:RequireMfaForAdmin` desligado.
- **Motivo obrigatório**, de 10 a 300 caracteres, como na impersonação, e **referência do chamado** de quem pediu.
- Nunca durante uma impersonação (`DenyDuringImpersonation`), nunca por um token de empresa, sem rota anônima e sem chave de automação.

## 3. O que faz

Uma única rota, por exemplo `POST /api/platform/tenants/{tenantId}/director-recovery`, com duas operações sobre **um usuário que já
existe nessa empresa**:

| Operação | De | Para |
|---|---|---|
| Reativar | Diretoria em `Inactive` ou `Blocked`, com e-mail confirmado | Diretoria em `Active` |
| Promover | outro papel em `Active`, com e-mail confirmado | Diretoria em `Active` |

**Condições, conferidas sob o lock da empresa** (`LastDirectorGuard.LockAsync`), no que está confirmado no banco naquele instante:

- A empresa tem **zero** Diretorias ativas. Com uma que seja, a resposta é `409`. Isso impede usar a rota para colocar mais um diretor numa
  empresa saudável.
- A empresa está `Active`. Suspensa ou cancelada, reativa-se primeiro, como na impersonação.
- O usuário alvo é dessa empresa e tem o e-mail confirmado. Quem está em `PendingVerification`, ou nunca confirmou
  (`User.EmailNeverVerified`), continua no caminho do link: a recuperação não pula a verificação de e-mail.

**O que nunca faz:**

- Não cria usuário. Se ninguém da empresa serve, a resposta é `422` e o caso sai do fluxo.
- Não redefine senha, não gera senha provisória e não envia link de acesso. Quem foi recuperado entra com a própria senha ou usa
  "Esqueci minha senha", que prova a posse do e-mail.
- Não desliga a verificação em duas etapas do alvo e não troca o e-mail dele.
- Não abre sessão para o administrador e não atua em lote.

**Efeito na sessão:** a mudança de papel troca o carimbo de segurança e encerra as sessões do alvo, como em `PUT /api/users/{id}`.

## 4. Como é auditado

- **Ação nova:** `platform_director_recovery`, severidade `critical`, com o rótulo nas duas telas (log de atividade da plataforma e
  auditoria da empresa). Não precisa de migração: cabe nas colunas de hoje.
- **Registro:** quem fez (id e e-mail do administrador), empresa, id do alvo, papel e situação antes e depois, operação, motivo,
  referência do chamado, IP e hora. Do alvo, só o id.
- **Na mesma transação da escrita** (`IAuditWriter.Stage`): a mudança e o registro entram juntos, ou nenhum dos dois.
- **Tentativa recusada** (empresa com Diretoria, alvo não confirmado, código errado) também fica registrada, em conexão própria, como
  `ILastDirectorRefusalAudit` faz: a recusa não confirma transação nenhuma e a auditoria não muda a resposta.
- A própria empresa vê o registro na tela de auditoria.

## 5. O que a empresa fica sabendo

Assim que a transação confirma, sai um e-mail para o contato da empresa (`Tenant.Email`), para o alvo e para os demais usuários ativos de
Gerência e Diretoria:

- O que mudou, quando e que foi feito pela administração da plataforma Orken.
- Quem passou a ser Diretoria.
- Como contestar: um canal de suporte, para desfazer se não foi pedido.
- Nenhum link de acesso, nenhuma credencial e nenhum dado além do necessário.

Falha de envio não desfaz a recuperação: ela fica no log e na auditoria, e a entrega é tentada de novo. Se nenhum provedor de e-mail
estiver configurado, a recomendação é recusar o ato (seção 9, item 6).

## 6. Como o abuso é limitado

- Só funciona com a empresa em zero Diretorias, e essa condição é conferida sob o lock.
- **Uma recuperação por empresa a cada 24 h.** Mais que isso exige outro `super_admin` ou o dono.
- **Limite por administrador** (por exemplo, 5 por dia) e limite de tentativas na rota, em uma política própria de `RateLimiting`.
- Três códigos TOTP errados encerram a tentativa por 15 minutos, como já acontece em outras verificações.
- Revisão mensal dos registros de `platform_director_recovery` pelo dono.
- **Testes obrigatórios quando for implementado:** empresa com Diretoria recusada, corrida entre duas recuperações, alvo de outra empresa,
  alvo não confirmado, `support` recusado, sessão sem duas etapas recusada, impersonação recusada, ausência de qualquer escrita em
  senha, MFA e criação de usuário, e rota listada em `RbacCoverageTests`.

## 7. Alternativas consideradas

| Alternativa | Vantagem | Problema |
|---|---|---|
| A. SQL manual (hoje) | Nenhum código | Sem auditoria, sem limite, sem aviso. Exige acesso ao banco de produção |
| B. Impersonar sem Diretoria e liberar `PUT /users` na impersonação | Pouco código | Precisa de um usuário para impersonar, e abre todos os poderes da Diretoria por 30 minutos para consertar uma coisa. Desfaz a decisão de negar esse `PUT` na impersonação |
| C. Rota própria e estreita na plataforma | Faz só isto, sob o lock, com auditoria, aviso e limites | Mais um poder sensível para proteger |
| D. Autoatendimento pelo e-mail de contato da empresa | Nenhum poder novo da plataforma | A caixa de contato pode estar com alguém que saiu da empresa, e ela decidiria quem manda na conta |
| E. Criar uma nova Diretoria | Resolve também quando ninguém serve | Excluída: cria acesso que a empresa não conhece |
| F. Redefinir a senha de uma Diretoria bloqueada | Reaproveita a rota de hoje | Excluída: quem administra passaria a conhecer uma credencial, e a empresa continuaria sem Diretoria ativa |

## 8. Recomendação

**Alternativa C**, com as regras acima. Ela é a única que entrega auditoria e limites sem aumentar o que um administrador pode fazer em uma
empresa saudável. A alternativa D pode vir depois, como complemento, se os chamados desse tipo forem frequentes.

## 9. O que o dono precisa decidir

1. **Duas pessoas ou uma.** Exigir aprovação de um segundo `super_admin`? É mais seguro, mas só funciona se houver dois ativos. Não
   conferi quantos existem em produção.
2. **Prova de quem pede.** O motivo é só um texto. Qual evidência é exigida antes do ato (documento do representante legal, contrato,
   pedido vindo do e-mail de contato)? Isso depende de orientação jurídica e dos termos de uso.
3. **Quem pode ser alvo.** Só quem já foi Diretoria (reativar) ou também outros papéis (promover)? Promover resolve mais casos, mas
   coloca a plataforma escolhendo quem passa a mandar na conta.
4. **Empresa suspensa ou cancelada.** A recomendação é exigir a reativação antes.
5. **Efeito imediato ou com espera.** A recomendação é imediato com aviso imediato, porque a empresa está parada. A alternativa é esperar,
   por exemplo, 24 h depois do aviso, para dar tempo de contestar.
6. **Sem provedor de e-mail.** A recomendação é recusar. A alternativa é permitir e registrar que o aviso não saiu.
7. **Os números de limite** da seção 6.
8. **O que a empresa lê do motivo.** O registro é visível para ela. O motivo pode citar dados do chamado: mostrar inteiro ou resumido?
9. **Onde fica a tela:** no console da plataforma deste ERP ou no Cockpit.
