# Nexo → Orken: o que mudou e o que fica

O produto se chama **Orken**. "Nexo" foi o nome de trabalho e ainda aparece por dentro do código. Este documento separa o que a pessoa usuária vê (corrigido) do que é interno (mantido de propósito), com o motivo e o custo de mudar cada item.

Regra usada: corrigir tudo o que alguém vê; manter o que é interno quando renomear traz risco sem benefício.

## Visível — corrigido

| Onde | Antes | Agora | PR |
|---|---|---|---|
| Interface (títulos, menus, telas, páginas públicas) | Nexo | Orken | rebrand anterior + conferência no PR 73 (nenhum texto visível com "Nexo") |
| Título da página e `og:title` | — | ORKEN | já estava |
| Remetente dos e-mails | — | `Orken <noreply@orken.com.br>` | já estava |
| Cabeçalho do recibo de venda | "Orken" fixo | nome do emissor da loja (Fiscal) ou da empresa | PR 71 |
| Nome da empresa na sessão de quem opera a plataforma | "NexoERP Platform" / "NexoERP" | "Orken Platform" / "Orken" | PR 73 |
| Título da documentação da API (Swagger, só fora de produção) | "Nexo API" | "Orken API" | PR 73 |

## Interno — mantido

| O quê | Exemplo | Por que fica | Custo de mudar |
|---|---|---|---|
| Namespaces e projetos .NET | `Nexo.Api`, `Nexo.Domain`… (~700 arquivos) | ninguém fora do time vê; o nome não aparece em resposta nem em tela | renomear projetos, soluções, pipelines e referências de uma vez; risco de quebrar build e histórico sem ganho ao cliente |
| Pastas do repositório | `nexo-backend/`, `nexo-main/` | caminhos usados pela Railway, pelo CI e pelos Dockerfiles | reconfigurar serviços na Railway (ação fora do código) |
| Banco de dados | schema `nexo`, banco/usuário locais `nexo` | o schema está em todas as migrações e tabelas de produção | migração que move todas as tabelas de schema — destrutiva no sentido operacional, sem benefício |
| Cookies de sessão | `nexo_access`, `nexo_refresh` | invisíveis na tela | renomear desloga todo mundo; exige período de transição lendo os dois nomes |
| Emissor e público do token (JWT) | `nexo-api`, `nexo-frontend`, `nexo-platform`, `nexo-refresh` | invisíveis; validados em cada requisição | trocar invalida todos os tokens emitidos (todos saem da conta) e mexe em configuração/segredos da implantação |
| Chaves do navegador (localStorage) | `nexo:session`, `nexo:access_token`, `nexo:refresh_token`, `nexo:impersonate`, `nexo:onboarding`, `nexo:pending_email`, `nexo:setup-dismissed` | invisíveis | trocar desloga quem está com o app aberto e esquece preferências |
| Política de CORS | `NexoFrontend` | nome interno da política | nenhum benefício |
| Classe CSS do carregamento inicial | `nexo-spinner` | interna | nenhum benefício |
| Arquivo de log no container | `logs/nexo-.log` | interno (e efêmero) | nenhum benefício |
| Pacote do frontend | `vite_react_shadcn_ts` | nome do template, não aparece | nenhum benefício |

## Quando valeria mudar o interno

- **Cookies, tokens e chaves do navegador:** junto de uma mudança que já obrigue todos a entrar de novo (por exemplo, trocar o domínio da API para `api.orken.com.br`), lendo os nomes antigos por um período.
- **Namespaces e pastas:** numa reorganização maior do repositório, nunca isoladamente.
- **Schema do banco:** não vale; o nome do schema não é visto por ninguém.
