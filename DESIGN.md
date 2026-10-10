# ORKEN: sistema de registros

## Cena de uso e tema
Um gestor consulta valores em um notebook no balcão iluminado enquanto atende sua equipe. Superfícies claras reduzem o contraste entre documentos físicos e a tela. A cozinha mantém contexto escuro específico; não introduzir um modo escuro global incompleto.

## Direção
Uma margem de navegação discreta, uma faixa de contexto e uma área de trabalho com cabeçalhos fortes e registros alinhados. A marca ORKEN usa letras compactas; o produto usa a mesma família em todas as funções. Nenhuma simulação de dados em superfícies públicas.

## Foundations
- Tipografia: Segoe UI Variable / Segoe UI, com alternativas nativas. Escolhida por legibilidade em Windows, português e ausência de download bloqueante. Public Sans foi avaliada, mas a fonte local evita dependência remota; Inter e Syne deixam de definir a identidade.
- Escala: 12px auxiliar, 14px operacional, 16px leitura, 20px seção, 28px página. Números tabulares.
- Cor: neutros levemente verdes, tinta escura, verde profundo para ação. Status: positivo, atenção, erro e informação com texto explícito.
- Tokens de cor mantêm interface HSL compatível com Tailwind/Radix. Valores da nova paleta são definidos centralmente; não mudar o formato de consumo em centenas de componentes.
- Espaço: 4, 8, 12, 16, 24, 32, 48px. Controles de 40px, mínimo 44px em interação por toque.
- Bordas: 1px. Radius 4px para controles, 6px para painéis, círculo apenas para identidade ou progresso.
- Elevação: somente overlays. Conteúdo separado por ritmo e linhas.
- Motion: resposta por cor e opacidade em 120ms; sem entradas coreografadas. Respeitar prefers-reduced-motion.

## Estrutura
Navegação contextual preserva módulos, roles e capabilities. Ctrl/Cmd+K abre destinos disponíveis, sem fingir busca universal de entidades. Empresa/loja sempre identificada. Drawer mobile com foco contido e Escape. Links de salto para conteúdo.

## Padrões
PageHeader identifica tarefa; SectionCard delimita um grupo real; TableShell e Table mantêm semântica; filtros têm rótulos; estados vazios distinguem primeiro uso de busca vazia; ErrorState oferece repetição. Valores monetários usam formatCurrency. Formulários preservam validação e ordem de teclado. Nenhuma confirmação transacional é removida.

## Conteúdo
Preferir “Clientes”, “Cadastrar cliente”, “Não foi possível carregar os clientes”. Não descrever a obviedade de um título. Não afirmar disponibilidade, segurança ou conformidade sem evidência.

## Validação
Comparar antes/depois em navegador. Dados sintéticos só no harness de teste e identificados como tal; não integrar mocks ao aplicativo. Executar typecheck, lint, testes, build e cenários responsivos. Registrar limites e falhas, inclusive anteriores à reconstrução.
