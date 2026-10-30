# Minha Obra: contrato temporário e integração pendente

O contrato `client-portal-contract.md` ainda não foi publicado. Confirmado pelo usuário em 30/10/2026. Este documento NÃO é um contrato aprovado do backend.

## Arquitetura

`nexo-main/src/modules/build-portal/types/index.ts` contém somente o modelo de apresentação marcado TEMPORARY CONTRACT. `api/gateway.ts` é a porta de integração. O adapter real deve validar/mapear respostas do OpenAPI para esse modelo, sem expor DTOs administrativos. Componentes não conhecem endpoints, tokens ou o formato de serialização do backend.

Ponto exato de substituição: `nexo-main/src/modules/build-portal/api/gateway.ts`, implementação `unavailableGateway` da interface `PortalGateway`. A injeção ocorre em `nexo-main/src/modules/build-portal/PortalPage.tsx`. Ainda não há diretório `hooks/`: os hooks de carregamento, cancelamento e sessão estão em `PortalSession`, nesse mesmo arquivo. O contrato provisório permanece inalterado até o alinhamento com o backend.

`route.ts` é o único resolvedor do caminho desejado `/{companySlug}/{projectSlug}`. Namespaces existentes são reservados. A superfície é montada fora de AuthProvider e WorkspaceProvider: login administrativo não autoriza o cliente público e não o redireciona para a plataforma. Mudança de slug destrói a sessão visual anterior; respostas atrasadas/abortadas são ignoradas.

Produção usa `unavailableGateway`, sem chamadas a endpoints inventados e sem dados simulados. Implementar o adapter real é requisito para lançamento. Nenhuma alteração de backend nesta branch.

## Contratos necessários ao backend

- Resolver publicação/acesso por ambos os slugs, de forma indistinguível para tenants/projetos incorretos. Identidade de tela privada somente se explicitamente segura para exposição pré-auth.
- Autenticação específica do portal, expiração, revogação, logout, política de CSRF/cookies/CORS, rate limit com Retry-After. Nunca reutilizar JWT administrativo. Senhas não persistidas no navegador.
- Payload autorizado contendo branding, progresso nullable, previsão/publicação, etapas, eventos passados e previstos, resumo semanal, documentos e URLs assinadas. Progresso ausente é desconhecido, nunca zero.
- Financeiro: nível explícito e dados filtrados no servidor. O adapter deve remover finance quando nível zero; a UI não solicita/transita por dados administrativos para decidir autorização.
- Decisões: ID/versionamento, opções, valores e impacto de prazo nullable; endpoint de confirmação com idempotência e conflito de versão. Histórico/estado de confirmação vem do servidor.
- Assistência: categorias autorizadas, solicitação, anexos com limites/MIME/tamanho e upload autorizado, disponibilidade, protocolo e status. Não há upload fictício em produção.
- GLB/GLTF autorizado, versão do modelo, IDs estáveis e binding explícito de ID para nó GLTF; pavimento/etapa/categoria; status por elemento. Snapshot tem estados completos por data; elementos ausentes permanecem desconhecidos. Definir formatos de compressão, URLs de recursos, limites e expiração.
- Reality capture: projeção panorâmica, URLs autorizadas, data, pavimento/zona e hotspots com destino e coordenadas. Clima: previsão e impactos já analisados/autorizados pelo backend. Nenhuma análise textual no cliente.
- Política SEO: indexação por portal e headers HTTP no servidor. O frontend usa noindex conservador, não publica título/metadata do projeto pré-auth. Proteção real requer autorização no backend e headers Cache-Control/no-store + X-Robots-Tag.

## Direção visual

Cliente abre o link no WhatsApp, em um celular sob luz ambiente, procurando tranquilidade sobre a obra. Superfície clara, tipografia legível e fotografia real em destaque; nome/identidade da construtora à frente, Orken só no rodapé. Hierarquia narrativa: progresso e previsão, publicação da semana, etapas, modelo, atualizações, imagens, escolhas, documentos, cuidado pós-entrega. Ausência de fotos não gera fotografia falsa. Acento de marca não compromete a legibilidade de botões.

## Viewer: escolha técnica

Three.js direto, carregado em chunk lazy somente após ação do usuário. Backend converte IFC; o browser não precisa processar IFC nem inferir bindings. GLTFLoader suporta configuração de decodificadores quando o formato real for conhecido. Rendering sob demanda, DPR limitado, disposal no unmount e fallback de erro/context loss. Não é comprovação de desempenho em GPU móvel ou modelos grandes.

Referências primárias consultadas: [GLTFLoader](https://threejs.org/docs/pages/GLTFLoader), [R3F performance](https://r3f.docs.pmnd.rs/advanced/scaling-performance), [xeokit viewer](https://github.com/xeolabs/xeokit-viewer/blob/master/README.md). R3F oferece integração declarativa, mas não é necessário para este canvas isolado. xeokit agrega formato/conversão e condições de distribuição que precisam ser avaliadas antes de adoção. Decisão: começar com GLTF renderizado por Three, adaptar quando houver modelo real.

## Revisão e execução

`VITE_BUILD_PORTAL_REVIEW=1` no servidor Vite de desenvolvimento habilita `/__review/minha-obra`. Nunca habilita em build de produção. Dados sintéticos estão exclusivamente em `dev/Review.tsx`, identificados na tela; testes usam callbacks locais de revisão, não chamadas reais.

- `npx vitest run src/modules/build-portal/portal.test.tsx`
- `npx playwright test --config playwright.build-portal.config.ts`
- `npm run typecheck`
- `npm run build -- --outDir node_modules/.build-portal-review`

Requer QA com backend real antes de publicação. Os testes de fixtures não certificam autenticação, autorização, confirmação financeira, entrega de anexos, TLS, cache ou exposição de dados no servidor.

## Estado da implementação em 30/10/2026

Branch dedicada, worktree isolado de origin/master 7dfe843. Frontend preparado para integração, não liberado para produção.

Implementado: shell público separado do ERP, rota customizada com namespaces reservados, estados de acesso, white label, home/progresso/previsão, etapas, publicações e resumo semanal, galeria foto/vídeo com filtros e comparação, financeiro por nível recebido, decisões com confirmação de impactos, documentos, entrega e garantias, assistência com formulário/anexos pela porta de integração, clima fornecido pelo adapter, visita panorâmica condicional e viewer GLTF.

Viewer: órbita/pan/zoom, reset/tela cheia, filtros de pavimento/etapa/categoria, corte horizontal, Projeto/Agora/Comparar, snapshots por seletor e controle temporal, seleção por raycast, evidências opcionais e lista textual equivalente. Trocar data/modo não baixa novamente o modelo. Bindings são explícitos por nome de nó no modelo de apresentação; o adapter real precisa transformar IDs estáveis do conversor nesse binding, sem inferência por nomes ou texto do diário.

Validação: 318 testes Vitest na suíte inteira (incluindo testes novos do portal); 11 testes Playwright em Edge, larguras 360/390/430/768/1440; typecheck estrito; ESLint dos arquivos alterados sem erros/avisos; build Vite. Um problema de ordem de inicialização de lazy no router foi encontrado no primeiro ciclo de navegador e corrigido antes da rodada aprovada. Modelo usado no teste de renderização é um triângulo GLTF sintético, não BIM real. Capturas mostram cenários de revisão identificados na tela.

Limites concretos para lançamento:
- O gateway de produção permanece indisponível até publicação e alinhamento do contrato real. Nenhum pagamento, decisão, autenticação de cliente ou anexo foi enviado a servidor neste trabalho.
- Rate limiting, revogação, isolamento por tenant/projeto e autorização de arquivos precisam ser exercitados contra o backend. A interface não substitui esses controles.
- Modelo real, elementos com hierarquia/instâncias, compressão Draco/Meshopt, chunks/progressive streaming, modelos grandes e GPU móvel exigem fixtures do conversor e profiling. Falhas atuais levam ao fallback; não há alegação de suporte a todo IFC/GLTF possível.
- Capturas 360 assumem panorama equiretangular. Hotspots aparecem como navegação acessível entre locais; coordenadas e overlay espacial dependem do contrato de captura.
- Não foi feita validação em iPhone/Android físico nem leitor de tela falado. Core Web Vitals de produção ainda precisam de medição.
- SEO privado é conservador no frontend; headers, cache, URLs assinadas e proteção do HTML/arquivos exigem servidor.

Sem merge, deploy ou alteração de backend. Não usar os callbacks de dev como implementação HTTP. A documentação do backend deve entrar em client-portal-contract.md; este arquivo tem nome distinto para não ser confundido com contrato aprovado.

Os logs de revisão preservam os resultados da execução; caminhos locais foram normalizados para referências relativas ao repositório.
