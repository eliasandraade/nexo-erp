# Roadmap e limitações

## Limitações atuais (v1)

| Área | Limitação | Risco |
|---|---|---|
| Catálogo | Só IBS/CBS de 2026 e regras de base do art. 12. 2027+ guardado como pendente/não verificado | cálculos de 2027+ saem sem alíquota (com aviso) |
| Regimes diferenciados/específicos | Itens marcados não são calculados | quem tem cesta básica, saúde, educação etc. vê só aviso |
| Classificação de itens | Produto não informa regime diferenciado nem NBS; vendas tratam tudo como regime geral | pode superestimar o tributo de itens com redução |
| Tributos no preço | Vendas não informam ICMS/ISS/PIS/Cofins embutidos | base superestimada (avisado) |
| Créditos | Ledger gerencial; elegibilidade declarada, não decidida; sem ligação automática com compras | depende de quem lança |
| Split payment | Nenhum provedor integrado; retenção real registrada à mão | conciliação manual |
| Evidências | Só descrição, referência e hash; nenhum arquivo | anexar documentos fica fora do sistema |
| Segregação de funções | O mesmo diretor pode revisar e aprovar | empresas pequenas têm um só diretor; avaliar exigir pessoas diferentes |
| Regras por tenant | Não há regras específicas de um tenant (benefício próprio, regime especial) | benefícios só como texto no perfil |
| Engine | Uma versão de avaliador; mudança de semântica exige preservar a anterior | documentado em rule-engine.md |
| Revisão jurídica | Catálogo e textos não passaram por profissional tributário | **necessária antes de produção** |
| Produto | Empacotamento comercial (módulo pago? plano?) não decidido | decisão do dono |

## Próximos passos

1. **Revisão profissional** do catálogo v1, dos requisitos do detector e dos textos das telas.
2. **Classificação fiscal de itens:** regime diferenciado/específico e NBS no cadastro de produto e serviço → condição por classificação.
3. **Reduções do art. 348, III, a** (e anexos da LC 214) como regras parametrizáveis, por classificação.
4. **Alíquotas de 2027+** assim que a resolução do Senado existir; ativar IBS 2027–2028 depois de verificar o Simples.
5. **Compras → créditos:** gerar `TaxCredit` a partir de compras com chave de NF-e; regra de elegibilidade versionada (art. 47, 57).
6. **Procurement:** usar `supplier-comparison` na tela de cotação de compras.
7. **Split payment:** adapter real quando houver API dos PSPs/adquirentes; importação de relatórios de liquidação.
8. **Mais detectores:** créditos não apropriados, retenção excedente no split (restituição), diferimento por data de fornecimento — sempre com natureza de impacto explícita.
9. **Evidências com arquivo** usando o storage privado (prefixo do tenant, URL assinada).
10. **Assistência por modelo de linguagem** (classificação de documentos, explicação de resultados) — sempre como sugestão revisada, nunca como cálculo.
11. **Cache do catálogo** por data quando o volume justificar.
12. **Regras por tenant** (benefícios e regimes especiais próprios) com o mesmo versionamento.
