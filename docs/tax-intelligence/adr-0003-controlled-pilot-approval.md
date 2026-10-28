# ADR 0003 — Aprovação interna para piloto controlado, separada da revisão profissional

- **Status:** aceita (decisão do Product Owner, 27/10/2026)
- **Data:** 27/10/2026

## Contexto

Pela ADR 0002, só regras com revisão profissional aprovada sustentavam uma decisão em produção. Nenhuma regra tem revisão profissional registrada, então o produto não podia ser usado nem em piloto. O Product Owner da Andrade Systems decidiu liberar o recorte da v1 para **piloto controlado**. É uma decisão interna de produto e governança (*INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT*), não um parecer profissional (*PROFESSIONAL TAX OPINION*).

Transformar `ProfessionalReview = Pending` em `Approved` seria falso. Desligar a política de aprovação perderia o controle sobre quais regras podem rodar.

## Decisão

1. **Nova dimensão por versão de regra: `InternalApproval`**, com os estados `NotApproved`, `ApprovedForControlledPilot` e `Withdrawn`. A aprovação registra:
   - a política (`orken-pilot-approval/1`);
   - quem registrou: o usuário autenticado da plataforma (id, nome exibido, capacidade) e a hora do servidor, nunca um nome digitado;
   - escopo e validade (dentro da vigência da versão);
   - as ressalvas aceitas, que são obrigatórias;
   - onde a decisão está registrada.

   Ela não toca a `ProfessionalReview`, e as duas coexistem. Só uma versão `Active` pode ser aprovada: `PendingRegulation`, `Unverified`, `Draft`, `Superseded` e `Revoked` continuam bloqueadas.
2. **Modo de aprovação** (`TaxIntelligence:Policy:Mode`):
   - `Disabled` (padrão) não aceita nenhuma aprovação. O piloto nunca é o modo padrão;
   - `ControlledPilot`, só por configuração explícita, aceita a aprovação interna ou uma profissional;
   - `ProfessionalFinal` aceita só a profissional.

   Piloto e produção definitiva são modos distintos e nunca se misturam.
3. **Execução fail-closed.** Trabalho novo só roda se:
   - o kill switch (`Enabled`, padrão `false`) estiver ligado;
   - o modo estiver configurado;
   - no piloto, a empresa ou loja estiver no escopo mantido pela plataforma (`TaxPilotScope`: janela, referência da decisão, quem ativou, participantes; vazio por padrão);
   - cada regra estiver tecnicamente `Active` **agora** e aprovada para o modo;
   - para uma decisão, o cálculo de origem ainda reproduzir.

   Uma versão revogada ou substituída mantém o registro da aprovação, mas não autoriza nada novo. O histórico continua legível e reproduzível.
4. **Registro por execução.** Cada cálculo grava, fora do input e do output com hash (que a engine V1 congelada não muda):
   - o modo;
   - a versão da política;
   - se a política estava ligada;
   - por regra, as duas aprovações e as ressalvas.

   A auditoria e os dossiês carregam os mesmos dados.
5. **A decisão não vira migration.** A migration `TaxControlledPilotGovernance` só cria o schema: tudo começa `NotApproved` e o escopo começa vazio. A decisão do Product Owner se efetiva por ações explícitas e auditadas da plataforma:
   - abrir o piloto;
   - admitir participantes;
   - aprovar versão a versão com a `DecisionReference`.

   Cada dossiê congela a referência exata de cada versão.
6. **Modo sombra.** O piloto não produz efeito fiscal externo. O módulo não depende de adaptador de documento fiscal, e o provedor de split só expõe status. Testes de arquitetura e de integração provam isso.

## Consequências

- O piloto roda com as ressalvas de cada regra visíveis em telas, cálculos e dossiês. A revisão profissional pendente continua aparecendo.
- Uma instalação nova ou atualizada não entra no piloto sozinha: fica desligada até a configuração e as ações explícitas.
- Para a produção definitiva, basta passar o modo para `ProfessionalFinal`. Nesse modo, nada roda até haver revisão profissional registrada, o que é o comportamento desejado.
- Retirar a aprovação interna de uma regra para imediatamente os cálculos e as decisões que dependem dela.
- Nenhuma tese tributária foi criada ou alterada, e nenhum fato jurídico foi reescrito para eliminar ressalvas.
- Registro da decisão: [homologation/internal-pilot-approval-2026.md](homologation/internal-pilot-approval-2026.md).
