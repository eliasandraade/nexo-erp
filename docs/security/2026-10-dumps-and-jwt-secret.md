# Dumps de banco no Git e segredo JWT (outubro/2026)

## 1. Dumps `nexo_backup.sql` e `nexo_backup_.sql`

| Item | Situação |
|---|---|
| **Conteúdo** | `pg_dump` 16.x de um banco de **desenvolvimento**, com 37 tabelas e quase idênticos entre si.<br>Trazem os dados demo do `DataSeeder`: Boutique Clara, Grupo Mix, Rede Norte, `admin@nexo.local`.<br>Também trazem **6 hashes bcrypt** (`$2a$12$`), incluindo o usuário de plataforma `elias@nexo.com`, e e-mails de contato como `contato@andradesystems.com.br`. |
| **Origem** | Commit `8e05975` ("redesign internal UI"). |
| **Uso** | Nenhum: nenhum código, script ou doc depende deles. |
| **Ação tomada** | Removidos do HEAD (`git rm --cached`; a cópia local continua no disco).<br>Padrões de dump adicionados ao `.gitignore`. |
| **O que NÃO foi feito** | Reescrita de histórico. Os dados **continuam no histórico do Git** e em todo clone ou fork existente. |

### Credenciais a rotacionar

1. **Usuário de plataforma `elias@nexo.com`.** Se a senha dele foi reutilizada em qualquer outro lugar, inclusive no super-admin de produção (`elias@orken.com.br`) ou em contas pessoais, troque-a.
2. **Usuários demo** (`admin`, `clara.boutique`, `lucas`, `ana`…). As senhas já estão em texto puro no `DataSeeder.cs`, então não há segredo a proteger. Garanta apenas que nenhum desses logins e senhas exista em produção (o seeder demo não roda em produção).

### Recomendação separada: purgar o histórico

Operação destrutiva e coordenada, a decidir pelo owner:
- `git filter-repo --path nexo_backup.sql --path nexo_backup_.sql --invert-paths`;
- force-push de todas as branches;
- todos os clones locais precisam ser refeitos;
- o cache de PR e fork do GitHub exige suporte do GitHub.

Só vale a pena se a rotação acima não for suficiente.

## 2. Segredo JWT em produção

- **Verificação (09/10/2026), sem expor valores.** Tokens assinados com os segredos versionados (o placeholder do `appsettings.json` e o de desenvolvimento) foram enviados à API de produção. Todos foram recusados na validação da assinatura (`WWW-Authenticate: … "The signature key was not found"`), exatamente como o token de controle. **Conclusão:** produção usa um `Jwt__Secret` próprio, que não é nenhum dos placeholders.
- **Hardening.** `JwtSecretPolicy` faz a API **recusar o startup** fora de Development/Testing quando o segredo:
  - está vazio;
  - tem menos de 32 caracteres;
  - é um valor versionado neste repositório;
  - contém marcadores de placeholder (`change_this`, `changeme`, `placeholder`…);
  - tem pouca variedade de caracteres.

  A mensagem de erro nunca inclui o segredo. Testes: `tests/Nexo.UnitTests/Auth/JwtSecretPolicyTests.cs`.
