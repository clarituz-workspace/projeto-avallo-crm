---
tags:
  - avallo
  - dashboard
  - relatorios
  - financeiro
status: concluido
ultima-revisao: 2026-08-03
---

# Painel e Relatorios

> Interface unificada para visualizar e analisar os dados financeiros de todos os marketplaces conectados ao tenant.

## Checklist funcional

- [x] Dashboard consolidado com dados de todos os marketplaces conectados
- [x] Faturamento total calculado pela soma de todos os marketplaces
- [x] Filtro por periodo
- [x] Filtro por plataforma ou conector
- [x] Filtro por forma de pagamento
- [x] Filtro por status
- [x] Card de resumo: Faturado
- [x] Card de resumo: Recebido
- [x] Card de resumo: Taxas
- [x] Card de resumo: A receber
- [x] Tabela unificada de lancamentos
- [x] Ordenacao de lancamentos
- [x] Busca por descricao ou ID externo
- [x] Paginacao dos lancamentos
- [x] Grafico de evolucao mensal
- [x] Grafico comparativo entre plataformas

## Evidencias da implementacao

### Visao consolidada

- A rota `/dashboard` apresenta os dados financeiros do tenant em uma unica interface.
- O endpoint `GET /api/reports/dashboard` consolida os lancamentos de todas as plataformas.
- A soma considera os filtros selecionados e permanece isolada pelo tenant autenticado.
- A tela tambem exibe os conectores ativos, a conta integrada, o estado da sincronizacao e a validade do acesso.

### Filtros

- Periodo inicial e final por seletor de intervalo de datas.
- Plataforma preenchida a partir dos conectores ativos.
- Forma de pagamento e status carregados a partir dos dados disponiveis no tenant.
- Os mesmos filtros sao aplicados aos cards, graficos, tabela e exportacoes.
- O backend valida o periodo informado antes de executar as consultas.

### Indicadores financeiros

| Card | Calculo apresentado |
|---|---|
| Faturado | Soma do valor bruto das vendas |
| Recebido | Soma dos valores liquidados |
| Taxas | Soma dos custos cobrados pelos canais |
| A receber | Saldo estimado ainda nao liquidado |

### Tabela de lancamentos

- Reune as movimentacoes financeiras de todos os canais.
- Exibe data, lancamento, plataforma, pagamento, status, faturado, taxas e valor a receber.
- Possui busca com debounce por descricao ou ID externo.
- A ordenacao e executada no backend para data, descricao, plataforma, status e valor faturado.
- A consulta e paginada em lotes de 25 registros.

### Graficos

- Grafico de linha para evolucao mensal de faturado e recebido.
- Grafico de barras para comparacao de faturado e recebido por plataforma.
- Ambos respeitam os filtros aplicados no painel.
- Estados vazios orientam o usuario quando ainda nao existem dados ou conectores ativos.

## Recursos adicionais concluidos

- [x] Exportacao consolidada em PDF
- [x] Exportacao em Excel com abas por marketplace
- [x] Exportacao em CSV para integracoes
- [x] Exportacao PDF separada por plataforma
- [x] Estados de carregamento com skeleton
- [x] Estado vazio para filtros sem resultados

## Criterio de aceite

O modulo e considerado concluido quando os dados de todos os marketplaces do tenant sao consolidados corretamente, os filtros afetam indicadores, graficos e lancamentos de forma consistente, e nenhuma consulta retorna dados pertencentes a outro tenant.

